using System.Windows;

namespace KlycKonsol;

public partial class App : Application
{
    Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        var guardArgs = Input.GuardLauncher.ParseArgs(e.Args.Prepend("exe").ToList());
        // Bekçiden açılıyorsak eski Konsol süreci çıkış yapıyor olabilir: kısa süre bekle.
        _single = new Mutex(false, "KLYC-Konsol-SingleInstance");
        var created = false;
        try { created = _single.WaitOne(guardArgs.Kind != Input.GuardStart.None ? 6000 : 0); }
        catch (AbandonedMutexException) { created = true; }
        if (!created)
        {
            Shutdown();
            return;
        }

        // Oyun bitti, Konsol'un açılması istenmiyor (otomatik dönüş kapalı): oynama süresini kaydet, ayarları geri yükle, çık.
        if (guardArgs.Kind == Input.GuardStart.GameExited && guardArgs.Silent)
        {
            var st = Library.AppState.Load();
            Input.SessionFinisher.RecordPlay(st, guardArgs.GameId, guardArgs.PlayedSeconds);
            Input.GameSession.End(st);
            Input.GuardLauncher.StopRunning();
            Input.GuardLauncher.Save(null);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Library.Loc.Configure(Library.AppState.Load().Language);

        // Konsol arayüzünde çökme olmasın: hatayı günlüğe yaz, çalışmaya devam et.
        DispatcherUnhandledException += (_, ex) =>
        {
            try
            {
                Directory.CreateDirectory(Library.AppPaths.Dir);
                File.AppendAllText(Path.Combine(Library.AppPaths.Dir, "crash.log"), $"{DateTime.Now:s}\n{ex.Exception}\n\n");
            }
            catch { }
            ex.Handled = true;
        };

        // Arayüz dışı (arka plan görevi / iş parçacığı) hataları da günlüğe yazılsın, sessizce kaybolmasın
        void Log(string kind, object? ex)
        {
            try { File.AppendAllText(Path.Combine(Library.AppPaths.Dir, "crash.log"), $"{DateTime.Now:s} {kind}\n{ex}\n\n"); } catch { }
        }
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log("görev", ex.Exception); ex.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log("işlem", ex.ExceptionObject);

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
