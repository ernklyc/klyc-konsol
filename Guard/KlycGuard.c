/*
 * KLYC Guard — oyun sırasında KLYC Konsol'un yerini alan küçük bekçi (≈ yüzlerce KB bellek, ~0 işlemci).
 *
 * Konsol bir oyun başlatınca (ya da arka plana inince) kendini tamamen kapatır ve bu programı başlatır. Guard:
 *   - Guide düğmesine, Start+Back'e, Ctrl+Alt+K kısayoluna ve bildirim alanı simgesine bakar → Konsol'u yeniden açar,
 *   - verilen oyun klasöründeki süreçleri izler; hepsi kapanınca Konsol'u "oyun bitti" bilgisiyle açar,
 *   - Konsol yeniden açılınca ("KlycKonsol.Guard.Stop" olayı) kendini kapatır.
 * Yönetici izni, ağ, dosya yazma, kayıt defteri yok.
 *
 * Argümanlar: --konsol <exe> [--dir <oyun klasörü>] [--game <kimlik>] [--started <unix sn>] [--autoreturn 0|1]
 *             [--firstseen <unix sn>] [--test-guide <dosya>]
 */
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <strsafe.h>
#include <dbt.h>
#include <wchar.h>
#include <stdlib.h>

#define WM_TRAY (WM_APP + 1)
#define ID_RESUME 1
#define ID_EXIT 2
#define MAX_TRACK 60
#define HOTKEY_ID 1

typedef struct { WORD buttons; BYTE lt, rt; SHORT lx, ly, rx, ry; } GAMEPAD;
typedef struct { DWORD packet; GAMEPAD pad; } XSTATE;
typedef DWORD (WINAPI *XGetStateEx)(DWORD, XSTATE *);

static wchar_t g_konsol[MAX_PATH * 2], g_dir[MAX_PATH * 2], g_game[256], g_testFile[MAX_PATH * 2];
static long long g_started, g_firstSeen;
static int g_autoReturn = 1;
static HWND g_wnd;
static HANDLE g_stop;
static HICON g_icon;
static BOOL g_trayAdded, g_hotkeyOk, g_quit;
static UINT g_taskbarCreated;
static ULONGLONG g_ignoreUntil;           /* Konsol az önce açıldıysa tekrar tetikleme */

/* ---- XInput ---------------------------------------------------------- */
static XGetStateEx g_xget;
static BOOL g_present[4], g_prevGuide[4], g_prevCombo[4];
static ULONGLONG g_nextRescan;
static volatile BOOL g_deviceChanged;   /* kol takıldı/çıkarıldı: bağlı olmayan yuvalar bir kez yeniden sorgulanır */

static void XInputInit(void)
{
    HMODULE h = LoadLibraryW(L"xinput1_4.dll");
    if (!h) h = LoadLibraryW(L"xinput1_3.dll");
    if (h) g_xget = (XGetStateEx)GetProcAddress(h, MAKEINTRESOURCEA(100));   /* XInputGetStateEx (Guide dahil) */
    for (int i = 0; i < 4; i++) { g_prevGuide[i] = TRUE; g_prevCombo[i] = TRUE; }   /* basılı başlarsa bırakılmadan tetikleme */
}

/* Guide ya da Start+Back'e yeni basıldı mı? Bağlı olmayan yuvalar yavaş sorgulanır: 2 sn'de bir bakılır. */
static BOOL PollPads(void)
{
    if (!g_xget) return FALSE;
    ULONGLONG now = GetTickCount64();
    /* Bağlı olmayan yuvaları sorgulamak pahalıdır: yalnızca aygıt değişince (ve 15 sn'de bir güvenlik için) bak. */
    BOOL rescan = (g_deviceChanged && now >= g_nextRescan) || now >= g_nextRescan + 15000;
    if (rescan) { g_deviceChanged = FALSE; g_nextRescan = now; }
    BOOL hit = FALSE;
    for (int i = 0; i < 4; i++)
    {
        if (!g_present[i] && !rescan) continue;
        XSTATE s;
        if (g_xget((DWORD)i, &s) != ERROR_SUCCESS) { g_present[i] = FALSE; g_prevGuide[i] = g_prevCombo[i] = FALSE; continue; }
        g_present[i] = TRUE;
        BOOL guide = (s.pad.buttons & 0x0400) != 0;
        BOOL combo = (s.pad.buttons & 0x0030) == 0x0030;
        if ((guide && !g_prevGuide[i]) || (combo && !g_prevCombo[i])) hit = TRUE;
        g_prevGuide[i] = guide;
        g_prevCombo[i] = combo;
    }
    return hit;
}

/* ---- Konsol'u başlat -------------------------------------------------- */
static void SpawnKonsol(const wchar_t *extra)
{
    wchar_t cmd[2048];
    StringCchPrintfW(cmd, 2048, L"\"%s\" --from-guard %s", g_konsol, extra);
    wchar_t cwd[MAX_PATH * 2];
    StringCchCopyW(cwd, MAX_PATH * 2, g_konsol);
    wchar_t *slash = wcsrchr(cwd, L'\\');
    if (slash) *slash = 0;
    AllowSetForegroundWindow(ASFW_ANY);
    STARTUPINFOW si = { sizeof si };
    PROCESS_INFORMATION pi;
    if (CreateProcessW(g_konsol, cmd, NULL, NULL, FALSE, 0, NULL, slash ? cwd : NULL, &si, &pi))
    {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

static long long NowUnix(void)
{
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER u = { ft.dwLowDateTime, ft.dwHighDateTime };
    return (long long)(u.QuadPart / 10000000ULL) - 11644473600LL;
}

static long long g_lastAliveUnix;

static void Resume(void)
{
    if (GetTickCount64() < g_ignoreUntil) return;
    g_ignoreUntil = GetTickCount64() + 6000;   /* Konsol açılırken art arda tetiklenmesin; açılınca bizi kapatır */
    wchar_t extra[128];
    StringCchPrintfW(extra, 128, L"--firstseen %lld", g_firstSeen);
    SpawnKonsol(extra);
}

static void FinishAndQuit(void)
{
    /* Oyun bitti / kullanıcı "çık" dedi: güç planı ve oynama süresi Konsol'un kendisi tarafından (sessizce) tamamlanır. */
    if (g_game[0])
    {
        long long played = (g_firstSeen > 0 && g_lastAliveUnix >= g_firstSeen) ? g_lastAliveUnix - g_firstSeen : 0;
        wchar_t extra[512];
        StringCchPrintfW(extra, 512, L"--game-exited --game \"%s\" --played %lld%s", g_game, played, g_autoReturn ? L"" : L" --silent");
        SpawnKonsol(extra);
    }
    g_quit = TRUE;
}

/* ---- Oyun süreçlerini izle ------------------------------------------- */
typedef struct { DWORD pid; HANDLE h; BOOL dead; } TRACKED;
static TRACKED g_tr[MAX_TRACK];
static int g_nTr;
static ULONGLONG g_lastNewTick, g_startedTick, g_nextScan, g_settledDeadline;
static BOOL g_watching, g_settledWait;

static BOOL StartsWithI(const wchar_t *s, const wchar_t *prefix)
{
    size_t n = wcslen(prefix);
    return _wcsnicmp(s, prefix, n) == 0;
}

static int Scan(void)
{
    int added = 0;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe = { sizeof pe };
    DWORD self = GetCurrentProcessId();
    for (BOOL ok = Process32FirstW(snap, &pe); ok && g_nTr < MAX_TRACK; ok = Process32NextW(snap, &pe))
    {
        DWORD pid = pe.th32ProcessID;
        if (pid == 0 || pid == self) continue;
        BOOL known = FALSE;
        for (int i = 0; i < g_nTr; i++) if (g_tr[i].pid == pid) { known = TRUE; break; }
        if (known) continue;
        HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid);
        if (!h) continue;
        wchar_t path[MAX_PATH * 2];
        DWORD len = MAX_PATH * 2;
        if (QueryFullProcessImageNameW(h, 0, path, &len) && StartsWithI(path, g_dir))
        {
            g_tr[g_nTr].pid = pid; g_tr[g_nTr].h = h; g_tr[g_nTr].dead = FALSE; g_nTr++; added++;
        }
        else CloseHandle(h);
    }
    CloseHandle(snap);
    return added;
}

static int AliveCount(void)
{
    int alive = 0;
    for (int i = 0; i < g_nTr; i++)
    {
        if (!g_tr[i].dead && WaitForSingleObject(g_tr[i].h, 0) == WAIT_OBJECT_0) g_tr[i].dead = TRUE;
        if (!g_tr[i].dead) alive++;
    }
    return alive;
}

static ULONGLONG GraceMs(void)
{
    /* Oyun yeni açılırken başlatıcı süreç kapanıp asıl oyun gecikmeli gelebilir: ilk 2 dk'da 20 sn, sonra 6 sn bekle */
    return (GetTickCount64() - g_startedTick < 120000ULL) ? 20000ULL : 6000ULL;
}

static void WatchStep(void)
{
    if (!g_watching) return;
    ULONGLONG now = GetTickCount64();

    if (g_settledWait)
    {
        /* Olay tabanlı bekleme: izlenenlerin hepsi kapanana dek buraya yalnızca ana döngü uyandığında gelinir */
        if (AliveCount() > 0) { g_lastAliveUnix = NowUnix(); g_settledDeadline = 0; return; }
        if (g_settledDeadline == 0) g_settledDeadline = now + GraceMs();
        if (now < g_settledDeadline) return;
        if (Scan() == 0 && AliveCount() == 0) { FinishAndQuit(); return; }
        g_lastNewTick = now; g_settledWait = FALSE; g_settledDeadline = 0;
        return;
    }

    if (now < g_nextScan) return;
    g_nextScan = now + 2000;
    if (Scan() > 0) { g_lastNewTick = now; if (g_firstSeen == 0) g_firstSeen = NowUnix(); }
    BOOL found = g_nTr > 0;
    int alive = found ? AliveCount() : 0;
    if (alive > 0) g_lastAliveUnix = NowUnix();

    if (!found && now - g_startedTick > 180000ULL) { g_watching = FALSE; return; }   /* hiç görünmedi: izlemeyi bırak */
    if (found && alive == 0 && now - g_lastNewTick > GraceMs()) { FinishAndQuit(); return; }
    if (found && now - g_lastNewTick > 25000ULL) g_settledWait = TRUE;
}

/* ---- Bildirim alanı --------------------------------------------------- */
static void TrayAdd(void)
{
    if (!g_icon) return;
    NOTIFYICONDATAW d = { sizeof d };
    d.hWnd = g_wnd; d.uID = 1; d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP; d.uCallbackMessage = WM_TRAY; d.hIcon = g_icon;
    StringCchCopyW(d.szTip, 128, L"KLYC Konsol — oyun modu (Guide / Ctrl+Alt+K ile aç)");
    g_trayAdded = Shell_NotifyIconW(NIM_ADD, &d);
}

static void TrayRemove(void)
{
    if (!g_trayAdded) return;
    NOTIFYICONDATAW d = { sizeof d };
    d.hWnd = g_wnd; d.uID = 1;
    Shell_NotifyIconW(NIM_DELETE, &d);
    g_trayAdded = FALSE;
}

static void ShowMenu(void)
{
    HMENU m = CreatePopupMenu();
    AppendMenuW(m, MF_STRING | MF_GRAYED, 99, g_game[0] ? L"KLYC Konsol — oyun modunda" : L"KLYC Konsol — arka planda");
    AppendMenuW(m, MF_SEPARATOR, 0, NULL);
    AppendMenuW(m, MF_STRING, ID_RESUME, L"Konsol'u aç");
    AppendMenuW(m, MF_SEPARATOR, 0, NULL);
    AppendMenuW(m, MF_STRING, ID_EXIT, L"Konsol'dan çık");
    POINT pt;
    GetCursorPos(&pt);
    SetForegroundWindow(g_wnd);
    int cmd = TrackPopupMenuEx(m, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.x, pt.y, g_wnd, NULL);
    PostMessageW(g_wnd, WM_NULL, 0, 0);
    DestroyMenu(m);
    if (cmd == ID_RESUME) Resume();
    else if (cmd == ID_EXIT) FinishAndQuit();
}

static LRESULT CALLBACK WndProc(HWND h, UINT msg, WPARAM w, LPARAM l)
{
    if (msg == g_taskbarCreated && g_taskbarCreated) { g_trayAdded = FALSE; TrayAdd(); return 0; }
    switch (msg)
    {
    case WM_TRAY:
        if (l == WM_LBUTTONUP) Resume();
        else if (l == WM_RBUTTONUP || l == WM_CONTEXTMENU) ShowMenu();
        return 0;
    case WM_DEVICECHANGE:
        if (w == DBT_DEVICEARRIVAL || w == DBT_DEVICEREMOVECOMPLETE) { g_deviceChanged = TRUE; g_nextRescan = GetTickCount64() + 800; }   /* kol hazır olsun diye kısa bekle */
        return TRUE;
    case WM_HOTKEY:
        if (w == HOTKEY_ID) Resume();
        return 0;
    }
    return DefWindowProcW(h, msg, w, l);
}

/* ---- Başlangıç -------------------------------------------------------- */
static void ParseArgs(void)
{
    int argc;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return;
    for (int i = 1; i < argc; i++)
    {
        const wchar_t *a = argv[i];
        const wchar_t *v = (i + 1 < argc) ? argv[i + 1] : L"";
        if (!wcscmp(a, L"--konsol")) { StringCchCopyW(g_konsol, MAX_PATH * 2, v); i++; }
        else if (!wcscmp(a, L"--dir"))
        {
            StringCchCopyW(g_dir, MAX_PATH * 2, v); i++;
            size_t n = wcslen(g_dir);
            if (n > 0 && g_dir[n - 1] != L'\\' && n + 1 < MAX_PATH * 2) { g_dir[n] = L'\\'; g_dir[n + 1] = 0; }
        }
        else if (!wcscmp(a, L"--game")) { StringCchCopyW(g_game, 256, v); i++; }
        else if (!wcscmp(a, L"--started")) { g_started = _wtoi64(v); i++; }
        else if (!wcscmp(a, L"--firstseen")) { g_firstSeen = _wtoi64(v); i++; }
        else if (!wcscmp(a, L"--autoreturn")) { g_autoReturn = _wtoi(v) != 0; i++; }
        else if (!wcscmp(a, L"--test-guide")) { StringCchCopyW(g_testFile, MAX_PATH * 2, v); i++; }
    }
    LocalFree(argv);
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR cmdline, int show)
{
    (void)prev; (void)cmdline; (void)show;
    ParseArgs();
    if (!g_konsol[0]) return 2;

    /* Tek bekçi: yenisi eskisinin yerini alır (eskisi durdurulur). */
    HANDLE once = CreateMutexW(NULL, FALSE, L"KlycKonsol.Guard");
    BOOL older = (GetLastError() == ERROR_ALREADY_EXISTS);
    g_stop = CreateEventW(NULL, TRUE, FALSE, L"KlycKonsol.Guard.Stop");
    if (older) { SetEvent(g_stop); Sleep(800); }
    ResetEvent(g_stop);
    (void)once;

    SetPriorityClass(GetCurrentProcess(), IDLE_PRIORITY_CLASS);
    PROCESS_POWER_THROTTLING_STATE pt = { PROCESS_POWER_THROTTLING_CURRENT_VERSION, PROCESS_POWER_THROTTLING_EXECUTION_SPEED, PROCESS_POWER_THROTTLING_EXECUTION_SPEED };
    SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &pt, sizeof pt);

    WNDCLASSW wc = { 0 };
    wc.lpfnWndProc = WndProc; wc.hInstance = inst; wc.lpszClassName = L"KlycGuardWnd";
    RegisterClassW(&wc);
    g_wnd = CreateWindowExW(0, wc.lpszClassName, L"KlycGuard", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, inst, NULL);
    g_taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");

    {
        /* Xbox kolları (XUSB) ve genel HID arabirimleri için aygıt bildirimi */
        static const GUID xusb = { 0xEC87F1E3, 0xC13B, 0x4100, { 0xB5, 0xF7, 0x8B, 0x84, 0xD5, 0x42, 0x60, 0xCB } };
        static const GUID hid = { 0x4D1E55B2, 0xF16F, 0x11CF, { 0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30 } };
        const GUID *guids[2] = { &xusb, &hid };
        for (int i = 0; i < 2; i++)
        {
            DEV_BROADCAST_DEVICEINTERFACE_W f = { sizeof f };
            f.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
            f.dbcc_classguid = *guids[i];
            RegisterDeviceNotificationW(g_wnd, &f, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
    }
    ExtractIconExW(g_konsol, 0, NULL, &g_icon, 1);
    TrayAdd();
    g_hotkeyOk = RegisterHotKey(g_wnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'K');
    XInputInit();
    g_deviceChanged = TRUE; g_nextRescan = GetTickCount64();   /* açılışta bir kez tara */

    g_startedTick = GetTickCount64() - (g_started > 0 ? (ULONGLONG)((NowUnix() - g_started) > 0 ? (NowUnix() - g_started) * 1000 : 0) : 0);
    g_lastNewTick = GetTickCount64();
    g_watching = g_dir[0] != 0 && g_game[0] != 0;
    g_nextScan = GetTickCount64() + 2000;
    if (g_firstSeen > 0) g_lastAliveUnix = NowUnix();

    ULONGLONG nextHotkeyTry = GetTickCount64() + 1000;
    ULONGLONG nextTrim = 0;
    while (!g_quit)
    {
        /* Bekle: durdurma olayı, izlenen süreçler (yerleşik modda) ya da 100 ms (Guide yoklaması) */
        HANDLE waits[1 + MAX_TRACK];
        int n = 0;
        waits[n++] = g_stop;
        if (g_settledWait) for (int i = 0; i < g_nTr; i++) if (!g_tr[i].dead) waits[n++] = g_tr[i].h;
        DWORD r = MsgWaitForMultipleObjects((DWORD)n, waits, FALSE, 100, QS_ALLINPUT);
        if (r == WAIT_OBJECT_0) break;   /* Konsol yeniden açıldı: bekçi görevini bıraktı */

        MSG msg;
        while (PeekMessageW(&msg, NULL, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }

        BOOL trigger = PollPads();
        if (g_testFile[0] && GetFileAttributesW(g_testFile) != INVALID_FILE_ATTRIBUTES) { DeleteFileW(g_testFile); trigger = TRUE; }
        if (trigger) Resume();
        if (!g_hotkeyOk && GetTickCount64() >= nextHotkeyTry)
        {
            nextHotkeyTry = GetTickCount64() + 1000;
            g_hotkeyOk = RegisterHotKey(g_wnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'K');
        }
        WatchStep();
        if (GetTickCount64() >= nextTrim)
        {
            nextTrim = GetTickCount64() + 30000;
            SetProcessWorkingSetSize(GetCurrentProcess(), (SIZE_T)-1, (SIZE_T)-1);   /* çalışma kümesini iade et */
        }
    }

    if (g_hotkeyOk) UnregisterHotKey(g_wnd, HOTKEY_ID);
    TrayRemove();
    if (g_icon) DestroyIcon(g_icon);
    return 0;
}
