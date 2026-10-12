namespace KlycKonsol;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Kurulum paketi (Velopack) kurulum/güncelleme/kaldırma sırasında uygulamayı özel parametrelerle çağırır; burada karşılanır.
        // Velopack ile kurulmamış (zip / geliştirme) çalıştırmada hiçbir şey yapmaz.
        Velopack.VelopackApp.Build().Run();

        // Başsız sınama kapıları (arayüz açılmaz): kurulu sürümün kendini güncelleyebildiğini gerçek kurulumla doğrulamak için
        if (Library.SelfUpdateTest.TryRun(args, out var code)) return code;

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}
