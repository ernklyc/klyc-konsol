/*
 * KLYC Shell — "Konsol modu" için Windows kabuğu yerine geçen küçük gözetmen (≈ yüzlerce KB, ~0 işlemci).
 *
 * Windows oturum açılınca kabuk olarak (HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell) bu program çalışır; masaüstü ve görev
 * çubuğu (explorer.exe) yerine KLYC Konsol başlatılır. Kabuk süreci kapanırsa Windows oturumu kapatır; o yüzden bu program HİÇ çıkmaz ve
 * bilgisayarı kullanılmaz bırakmamak için çok katmanlı bir geri dönüş uygular:
 *   1. Safe Mode, ya da oturum açılırken SHIFT basılı, ya da %AppData%\KLYC-Konsol\shell.off dosyası varsa, ya da Konsol dosyası yoksa:
 *      doğrudan Windows masaüstü (explorer.exe) başlatılır.
 *   2. Konsol (ya da oyun sırasında bekçi KlycGuard) çalışırken beklenir. Hiçbiri yoksa ve masaüstü de açık değilse Konsol yeniden başlatılır
 *      (zaman penceresinde en çok N kez); sınır aşılırsa (çökme döngüsü) masaüstü başlatılır.
 *   3. Kullanıcı Konsol'dan "Windows masaüstüne geç" derse Konsol explorer.exe'yi kendisi açar; masaüstü açıkken bu program yalnızca bekler.
 * Son çare: Ctrl+Alt+Del → Görev Yöneticisi → Yeni görev → explorer.exe.
 * Yönetici izni, ağ yok. Günlük: %AppData%\KLYC-Konsol\shell.log (küçük, sınırlı).
 *
 * Argümanlar: --konsol <exe> [--desktop <exe>] [--task <görev adı>] [--poll <ms>] [--max <n>] [--window <sn>] [--miss <n>]
 */
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <tlhelp32.h>
#include <shellapi.h>
#include <shlobj.h>
#include <strsafe.h>
#include <wchar.h>
#include <stdlib.h>

static wchar_t g_konsol[MAX_PATH * 2], g_desktop[MAX_PATH * 2], g_task[128], g_desktopName[64] = L"explorer.exe", g_log[MAX_PATH * 2];
static DWORD g_poll = 2000, g_max = 3, g_window = 120, g_miss = 3;

static void Log(const wchar_t *msg)
{
    if (!g_log[0]) return;
    HANDLE h = CreateFileW(g_log, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER sz;
    if (GetFileSizeEx(h, &sz) && sz.QuadPart > 200 * 1024) { CloseHandle(h); DeleteFileW(g_log); h = CreateFileW(g_log, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL); if (h == INVALID_HANDLE_VALUE) return; }
    SYSTEMTIME t; GetLocalTime(&t);
    wchar_t line[512];
    StringCchPrintfW(line, 512, L"%04d-%02d-%02d %02d:%02d:%02d %s\r\n", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, msg);
    char u8[1536]; int n8 = WideCharToMultiByte(CP_UTF8, 0, line, -1, u8, sizeof u8, NULL, NULL);
    DWORD w; if (n8 > 1) WriteFile(h, u8, (DWORD)(n8 - 1), &w, NULL);   /* UTF-8 */
    CloseHandle(h);
}

static BOOL FileExists(const wchar_t *p) { DWORD a = GetFileAttributesW(p); return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY); }

static const wchar_t *BaseName(const wchar_t *p) { const wchar_t *s = wcsrchr(p, L'\\'); return s ? s + 1 : p; }

/* Bu oturumdaki, adı eşleşen süreç var mı? */
static BOOL Running(const wchar_t *name)
{
    DWORD mySession = 0; ProcessIdToSessionId(GetCurrentProcessId(), &mySession);
    HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (s == INVALID_HANDLE_VALUE) return FALSE;
    PROCESSENTRY32W e; e.dwSize = sizeof e; BOOL found = FALSE;
    if (Process32FirstW(s, &e)) do {
        if (_wcsicmp(e.szExeFile, name) == 0) {
            DWORD sess = 0;
            if (ProcessIdToSessionId(e.th32ProcessID, &sess) && sess == mySession) { found = TRUE; break; }
        }
    } while (Process32NextW(s, &e));
    CloseHandle(s);
    return found;
}

static BOOL Spawn(const wchar_t *exe, const wchar_t *args)
{
    wchar_t cmd[MAX_PATH * 3];
    StringCchPrintfW(cmd, MAX_PATH * 3, L"\"%s\"%s%s", exe, args && args[0] ? L" " : L"", args ? args : L"");
    STARTUPINFOW si; PROCESS_INFORMATION pi; ZeroMemory(&si, sizeof si); si.cb = sizeof si;
    wchar_t dir[MAX_PATH * 2]; StringCchCopyW(dir, MAX_PATH * 2, exe);
    wchar_t *sl = wcsrchr(dir, L'\\'); if (sl) *sl = 0; else dir[0] = 0;
    if (!CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, dir[0] ? dir : NULL, &si, &pi)) return FALSE;
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return TRUE;
}

static void StartDesktop(void)
{
    if (Running(g_desktopName)) return;
    Log(L"Windows masaüstü başlatılıyor");
    if (!Spawn(g_desktop, L"")) Log(L"masaüstü başlatılamadı");
}

static void StartKonsol(void)
{
    if (g_task[0]) {   /* yönetici olarak açma (UAC'siz): zamanlanmış görev */
        wchar_t a[256]; StringCchPrintfW(a, 256, L"/run /tn \"%s\"", g_task);
        wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
        wchar_t sch[MAX_PATH * 2]; StringCchPrintfW(sch, MAX_PATH * 2, L"%s\\schtasks.exe", sys);
        STARTUPINFOW si; PROCESS_INFORMATION pi; ZeroMemory(&si, sizeof si); si.cb = sizeof si; si.dwFlags = STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE;
        wchar_t cmd[MAX_PATH * 3]; StringCchPrintfW(cmd, MAX_PATH * 3, L"\"%s\" %s", sch, a);
        if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 15000); DWORD code = 1; GetExitCodeProcess(pi.hProcess, &code);
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            if (code == 0) { Log(L"Konsol zamanlanmış görevle başlatıldı"); return; }
        }
        Log(L"görev başlatılamadı, Konsol doğrudan başlatılıyor");
    }
    if (Spawn(g_konsol, L"")) Log(L"Konsol başlatıldı"); else Log(L"Konsol başlatılamadı");
}

static BOOL Fallback(void)
{
    wchar_t off[MAX_PATH * 2], ad[MAX_PATH];
    if (GetSystemMetrics(SM_CLEANBOOT)) { Log(L"Safe Mode: masaüstü"); return TRUE; }
    if (GetAsyncKeyState(VK_SHIFT) & 0x8000) { Log(L"SHIFT basılı: masaüstü"); return TRUE; }
    if (!FileExists(g_konsol)) { Log(L"Konsol bulunamadı: masaüstü"); return TRUE; }
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_APPDATA, NULL, 0, ad))) {
        StringCchPrintfW(off, MAX_PATH * 2, L"%s\\KLYC-Konsol\\shell.off", ad);
        if (FileExists(off)) { Log(L"shell.off var: masaüstü"); return TRUE; }
    }
    return FALSE;
}

static void ParseArgs(void)
{
    int n = 0; wchar_t **a = CommandLineToArgvW(GetCommandLineW(), &n);
    if (!a) return;
    for (int i = 1; i < n; i++) {
        if (_wcsicmp(a[i], L"--konsol") == 0 && i + 1 < n) StringCchCopyW(g_konsol, MAX_PATH * 2, a[++i]);
        else if (_wcsicmp(a[i], L"--desktop") == 0 && i + 1 < n) StringCchCopyW(g_desktop, MAX_PATH * 2, a[++i]);
        else if (_wcsicmp(a[i], L"--task") == 0 && i + 1 < n) StringCchCopyW(g_task, 128, a[++i]);
        else if (_wcsicmp(a[i], L"--poll") == 0 && i + 1 < n) g_poll = (DWORD)_wtoi(a[++i]);
        else if (_wcsicmp(a[i], L"--max") == 0 && i + 1 < n) g_max = (DWORD)_wtoi(a[++i]);
        else if (_wcsicmp(a[i], L"--window") == 0 && i + 1 < n) g_window = (DWORD)_wtoi(a[++i]);
        else if (_wcsicmp(a[i], L"--miss") == 0 && i + 1 < n) g_miss = (DWORD)_wtoi(a[++i]);
    }
    LocalFree(a);
    if (g_poll < 100) g_poll = 100;
    if (!g_desktop[0]) { wchar_t w[MAX_PATH]; GetWindowsDirectoryW(w, MAX_PATH); StringCchPrintfW(g_desktop, MAX_PATH * 2, L"%s\\explorer.exe", w); }
    StringCchCopyW(g_desktopName, 64, BaseName(g_desktop));
}

int WINAPI wWinMain(HINSTANCE h, HINSTANCE p, LPWSTR cl, int show)
{
    (void)h; (void)p; (void)cl; (void)show;
    HANDLE mtx = CreateMutexW(NULL, TRUE, L"KlycShell-Single");
    if (GetLastError() == ERROR_ALREADY_EXISTS) return 0;   /* ikinci kopya: bırak */

    wchar_t ad[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_APPDATA, NULL, 0, ad))) {
        wchar_t dir[MAX_PATH * 2]; StringCchPrintfW(dir, MAX_PATH * 2, L"%s\\KLYC-Konsol", ad);
        CreateDirectoryW(dir, NULL);
        StringCchPrintfW(g_log, MAX_PATH * 2, L"%s\\shell.log", dir);
    }
    ParseArgs();
    Log(L"KLYC Shell başladı");

    ULONGLONG starts[16]; DWORD nStarts = 0;
    DWORD misses = 0;
    BOOL desktopMode = Fallback();
    if (desktopMode) StartDesktop(); else { StartKonsol(); starts[nStarts++] = GetTickCount64(); }

    for (;;) {
        Sleep(g_poll);
        BOOL desk = Running(g_desktopName);
        BOOL kon = Running(L"KlycKonsol.exe") || Running(L"KlycGuard.exe");
        if (desk || kon) { misses = 0; if (desk && !kon) desktopMode = TRUE; if (kon) desktopMode = FALSE; continue; }
        if (desktopMode) { if (++misses >= g_miss) { misses = 0; StartDesktop(); } continue; }
        if (++misses < g_miss) continue;   /* kısa boşluk (bekçi devri, Konsol yeniden açılışı): bekle */
        misses = 0;
        /* zaman penceresindeki başlatmaları say */
        ULONGLONG now = GetTickCount64(); DWORD k = 0;
        for (DWORD i = 0; i < nStarts; i++) if (now - starts[i] < (ULONGLONG)g_window * 1000) starts[k++] = starts[i];
        nStarts = k;
        if (nStarts >= g_max) { Log(L"Konsol tekrar tekrar kapandı: masaüstüne dönülüyor"); desktopMode = TRUE; StartDesktop(); continue; }
        Log(L"Konsol çalışmıyor: yeniden başlatılıyor");
        StartKonsol();
        if (nStarts < 16) starts[nStarts++] = GetTickCount64();
    }
    (void)mtx;
    return 0;
}
