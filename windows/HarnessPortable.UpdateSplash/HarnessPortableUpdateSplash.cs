using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static int Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch { try { SetProcessDPIAware(); } catch { } }

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HarnessSplashForm(SplashConfig.Parse(args)));
            return 0;
        }
        catch { return 1; } // The parent falls back to native progress if rendering fails.
    }
}

internal sealed class SplashConfig
{
    public string Title = "Harness Portable";
    public string FromVersion = "";
    public string ToVersion = "";
    public string Theme = "Light"; // Light | Dark (matches DshTokens.Light.xaml / DshTokens.Dark.xaml)
    public string LogoPath = "";
    public int WaitPid;
    public string RootDir = "";
    public string MainExe = "HarnessPortable.exe";
    public int TimeoutMs = 90000;
    public int DemoMs;
    public int DemoStage = 1;
    public string ScreenshotPath = "";
    public string SessionDir = "";
    public bool Offscreen;

    public static SplashConfig Parse(string[] args)
    {
        var c = new SplashConfig();
        for (int i = 0; i < args.Length; i++)
        {
            string k = args[i];
            string v = (i + 1 < args.Length) ? args[i + 1] : "";
            switch (k)
            {
                case "--title": c.Title = v; i++; break;
                case "--from-version": c.FromVersion = v; i++; break;
                case "--to-version": c.ToVersion = v; i++; break;
                case "--theme": c.Theme = v; i++; break;
                case "--logo": c.LogoPath = v; i++; break;
                case "--wait-pid": int.TryParse(v, out c.WaitPid); i++; break;
                case "--root-dir": c.RootDir = v; i++; break;
                case "--main-exe": c.MainExe = v; i++; break;
                case "--timeout-ms": int.TryParse(v, out c.TimeoutMs); i++; break;
                case "--demo-ms": int.TryParse(v, out c.DemoMs); i++; break;
                case "--demo-stage": int.TryParse(v, out c.DemoStage); i++; break;
                case "--screenshot": c.ScreenshotPath = v; i++; break;
                case "--session-dir": c.SessionDir = v; i++; break;
                case "--offscreen": c.Offscreen = true; break;
            }
        }
        return c;
    }
}

internal sealed class HarnessSplashForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private readonly SplashConfig config;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Timer animTimer;
    private readonly Timer probeTimer;
    private Image logoImage;
    private float scale = 1f;
    private bool dwmRounded;
    private bool isDark;

    private int stage; // 0: closing, 1: extracting, 2: replacing, 3: starting
    private float targetProgress = 0.16f;
    private float displayProgress = 0.10f;
    private float shimmerPhase;
    private bool sawTempDir;
    private bool sawSwapOrMultipleTemp;
    private DateTime? updateExeExitedAt;
    private bool screenshotSaved;
    private readonly DateTime launchedAt = DateTime.UtcNow;
    private string lastState;

    // Exact DshTokens palette from HarnessPortable.Windows
    private Color bgBase;
    private Color bgLayer1;
    private Color bgLayer2;
    private Color borderL1;
    private Color borderL2;
    private Color textPrimary;
    private Color textSecondary;
    private Color textTertiary;
    private Color accent;
    private Color accentEnd;
    private Color accentSoft;
    private Color dshGreen;

    public HarnessSplashForm(SplashConfig config)
    {
        this.config = config;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        // Status only: no drag gesture, controls, activation, or focus transfer.
        ShowInTaskbar = false;
        TopMost = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Text = config.Title + " · 正在升级";
        ApplyTheme();
        LoadLogo();

        if (!string.IsNullOrEmpty(config.ScreenshotPath))
        {
            stage = Math.Max(0, Math.Min(3, config.DemoStage));
            targetProgress = StageBaseProgress(stage) + 0.12f;
            displayProgress = targetProgress;
            shimmerPhase = 0.46f;
        }
        else if (config.DemoMs > 0)
        {
            stage = 1;
            targetProgress = StageBaseProgress(1);
            displayProgress = 0.14f;
        }

        animTimer = new Timer { Interval = 16 };
        animTimer.Tick += OnAnimateTick;
        probeTimer = new Timer { Interval = 100 };
        probeTimer.Tick += OnProbeTick;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using (Graphics g = CreateGraphics())
        {
            scale = g.DpiX / 96f;
            if (scale <= 0f) scale = 1f;
        }
        if (!string.IsNullOrEmpty(config.ScreenshotPath) && scale < 1.25f)
            scale = 1.25f;
        ClientSize = new Size(S(484), S(188));
        TryEnableDwmCornerAndTheme();
        if (!dwmRounded)
            Region = new Region(RoundedRect(new RectangleF(0, 0, Width, Height), S(14)));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (config.Offscreen) Location = new Point(-30000, 0);
        else CenterToScreen();
        Update();
        WriteState("closing");
        if (!string.IsNullOrEmpty(config.SessionDir))
            File.WriteAllText(Path.Combine(config.SessionDir, "ready"), Handle.ToInt64().ToString());
        animTimer.Start();
        probeTimer.Start();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    private void Signal(string name)
    {
        if (string.IsNullOrEmpty(config.SessionDir)) return;
        try { File.WriteAllText(Path.Combine(config.SessionDir, name), ""); } catch { }
    }

    private void WriteState(string value)
    {
        if (value == lastState) return;
        lastState = value;
        Signal("observed-" + value);
        if (string.IsNullOrEmpty(config.SessionDir)) return;
        try { File.WriteAllText(Path.Combine(config.SessionDir, "state"), value); } catch { }
    }

    private void ApplyTheme()
    {
        isDark = string.Equals((config.Theme ?? "").Trim(), "Dark", StringComparison.OrdinalIgnoreCase);
        if (isDark)
        {
            // DshTokens.Dark.xaml
            bgBase = Color.FromArgb(16, 23, 34);          // #101722
            bgLayer1 = Color.FromArgb(24, 34, 49);        // #182231
            bgLayer2 = Color.FromArgb(32, 45, 64);        // #202D40
            borderL1 = Color.FromArgb(43, 58, 80);        // #2B3A50
            borderL2 = Color.FromArgb(53, 69, 93);        // #35455D
            textPrimary = Color.FromArgb(237, 242, 251);  // #EDF2FB
            textSecondary = Color.FromArgb(167, 181, 204);// #A7B5CC
            textTertiary = Color.FromArgb(146, 161, 186); // #92A1BA
            accent = Color.FromArgb(77, 107, 254);        // #4D6BFE
            accentEnd = Color.FromArgb(142, 175, 255);    // #8EAFFF
            accentSoft = Color.FromArgb(38, 56, 88);      // #263858
            dshGreen = Color.FromArgb(75, 210, 160);      // #4BD2A0
        }
        else
        {
            // DshTokens.Light.xaml
            bgBase = Color.FromArgb(243, 245, 249);       // #F3F5F9
            bgLayer1 = Color.FromArgb(255, 255, 255);     // #FFFFFF
            bgLayer2 = Color.FromArgb(237, 241, 247);     // #EDF1F7
            borderL1 = Color.FromArgb(223, 229, 239);     // #DFE5EF
            borderL2 = Color.FromArgb(207, 216, 230);     // #CFD8E6
            textPrimary = Color.FromArgb(23, 34, 57);     // #172239
            textSecondary = Color.FromArgb(82, 96, 120);  // #526078
            textTertiary = Color.FromArgb(94, 107, 131);  // #5E6B83
            accent = Color.FromArgb(54, 94, 220);         // #365EDC
            accentEnd = Color.FromArgb(77, 107, 254);     // #4D6BFE
            accentSoft = Color.FromArgb(233, 238, 255);   // #E9EEFF
            dshGreen = Color.FromArgb(21, 128, 95);       // #15805F
        }
        BackColor = bgBase;
    }

    private void LoadLogo()
    {
        try
        {
            if (!string.IsNullOrEmpty(config.LogoPath) && File.Exists(config.LogoPath))
            {
                if (config.LogoPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using (var ico = new Icon(config.LogoPath, new Size(256, 256)))
                    {
                        Icon = (Icon)ico.Clone();
                        logoImage = ico.ToBitmap();
                    }
                }
                else
                {
                    using (var fs = new FileStream(config.LogoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var decoded = Image.FromStream(fs))
                        logoImage = new Bitmap(decoded);
                }
            }
        }
        catch { }
    }

    private void TryEnableDwmCornerAndTheme()
    {
        try
        {
            int dark = isDark ? 1 : 0;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int corner = 2;
            int hr = DwmSetWindowAttribute(Handle, 33, ref corner, 4);
            dwmRounded = (hr == 0);
        }
        catch { dwmRounded = false; }
    }

    private static float StageBaseProgress(int s)
    {
        switch (s)
        {
            case 0: return 0.14f;
            case 1: return 0.36f;
            case 2: return 0.68f;
            default: return 0.90f;
        }
    }

    private static float StageMaxProgress(int s)
    {
        switch (s)
        {
            case 0: return 0.24f;
            case 1: return 0.62f;
            case 2: return 0.86f;
            default: return 0.96f;
        }
    }

    private string StageLabel()
    {
        switch (stage)
        {
            case 0: return "正在退出当前版本并保存工作区…";
            case 1: return "正在解压新版本程序文件…";
            case 2: return "正在替换客户端二进制文件并清理旧版本…";
            default: return "正在启动新版本，按已保存的设置恢复工作区…";
        }
    }

    private void OnAnimateTick(object sender, EventArgs e)
    {
        shimmerPhase += 0.028f;
        if (shimmerPhase > 1.4f) shimmerPhase = -0.4f;

        if (config.DemoMs > 0 && string.IsNullOrEmpty(config.ScreenshotPath))
        {
            float ratio = Math.Min(1f, (float)elapsed.ElapsedMilliseconds / Math.Max(1000, config.DemoMs));
            stage = ratio < 0.42f ? 1 : (ratio < 0.78f ? 2 : 3);
            targetProgress = Math.Min(1f, 0.14f + ratio * 0.86f);
            displayProgress += (targetProgress - displayProgress) * 0.12f;
        }
        else if (config.DemoMs == 0 && string.IsNullOrEmpty(config.ScreenshotPath))
        {
            float maxForStage = StageMaxProgress(stage);
            if (targetProgress < maxForStage)
                targetProgress = Math.Min(maxForStage, targetProgress + 0.0018f);
            displayProgress += (targetProgress - displayProgress) * 0.12f;
        }

        Invalidate();

        if (!screenshotSaved && !string.IsNullOrEmpty(config.ScreenshotPath) && elapsed.ElapsedMilliseconds >= 120)
        {
            screenshotSaved = true;
            SaveSnapshot(config.ScreenshotPath);
            if (config.DemoMs <= 0) { Close(); return; }
        }
        if (config.DemoMs > 0 && elapsed.ElapsedMilliseconds >= config.DemoMs)
            Close();
    }

    private void OnProbeTick(object sender, EventArgs e)
    {
        if (config.DemoMs > 0 || !string.IsNullOrEmpty(config.ScreenshotPath)) return;
        if (!string.IsNullOrEmpty(config.SessionDir) && File.Exists(Path.Combine(config.SessionDir, "cancel")))
        { WriteState("cancelled"); Close(); return; }
        if (elapsed.ElapsedMilliseconds > config.TimeoutMs) { WriteState("timeout"); Close(); return; }

        if (IsPidAlive(config.WaitPid)) { stage = 0; WriteState("closing"); return; }
        if (stage < 1) { stage = 1; targetProgress = Math.Max(targetProgress, StageBaseProgress(1)); }

        if (!string.IsNullOrEmpty(config.RootDir))
        {
            try
            {
                string veloTemp = Path.Combine(config.RootDir, "packages", "VelopackTemp");
                if (Directory.Exists(veloTemp))
                {
                    int subDirs = Directory.GetDirectories(veloTemp).Length;
                    if (subDirs >= 1) sawTempDir = true;
                    if (subDirs >= 2 || (sawTempDir && subDirs == 0)) sawSwapOrMultipleTemp = true;
                }
                else if (sawTempDir) sawSwapOrMultipleTemp = true;
            }
            catch { }
        }

        if (sawSwapOrMultipleTemp && stage < 2)
        {
            stage = 2;
            targetProgress = Math.Max(targetProgress, StageBaseProgress(2));
        }

        if (!IsUpdateExeRunning(config.RootDir) && stage >= 1)
        {
            if (stage < 3) { stage = 3; targetProgress = Math.Max(targetProgress, StageBaseProgress(3)); }
            if (updateExeExitedAt == null) updateExeExitedAt = DateTime.UtcNow;
        }

        IntPtr newMainWindow = FindNewMainWindow();
        if (newMainWindow != IntPtr.Zero)
        {
            displayProgress = 1f;
            Invalidate(); Update();
            WriteState("handoff");
            Close();
            return;
        }

        if (updateExeExitedAt != null)
        {
            double sinceExitMs = (DateTime.UtcNow - updateExeExitedAt.Value).TotalMilliseconds;
            if (sinceExitMs >= 5000 && !IsMainExeRunning())
            { WriteState("updater-exited"); Close(); return; }
        }
        WriteState(stage == 1 ? "extracting" : (stage == 2 ? "replacing" : "starting"));
    }

    private static bool IsPidAlive(int pid)
    {
        if (pid <= 0) return false;
        try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; }
        catch { return false; }
    }

    private static bool IsUpdateExeRunning(string rootDir)
    {
        try
        {
            var procs = Process.GetProcessesByName("Update");
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        string path = p.MainModule != null ? p.MainModule.FileName : null;
                        if (IsInsideRoot(path, rootDir))
                            return true;
                    }
                    catch { }
                }
                return false;
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { return false; }
    }

    private string TargetProcessName()
    {
        string name = config.MainExe ?? "HarnessPortable";
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }

    private bool IsMainExeRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName(TargetProcessName());
            try { return procs.Any(IsTargetProcess); }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { return false; }
    }

    private IntPtr FindNewMainWindow()
    {
        try
        {
            var procs = Process.GetProcessesByName(TargetProcessName());
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (!IsTargetProcess(p)) continue;
                        IntPtr hwnd = p.MainWindowHandle;
                        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd)) return hwnd;
                    }
                    catch { }
                }
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { }
        return IntPtr.Zero;
    }

    private static bool IsInsideRoot(string path, string rootDir)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rootDir)) return false;
        try
        {
            var root = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private bool IsTargetProcess(Process process)
    {
        try
        {
            return process.Id != config.WaitPid && !process.HasExited &&
                process.StartTime.ToUniversalTime() >= launchedAt &&
                IsInsideRoot(process.MainModule.FileName, config.RootDir);
        }
        catch { return false; }
    }

    private void SaveSnapshot(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    RenderSurface(g, new Rectangle(0, 0, bmp.Width, bmp.Height), true);
                bmp.Save(path, ImageFormat.Png);
            }
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RenderSurface(e.Graphics, ClientRectangle, false);
    }

    private void RenderSurface(Graphics g, Rectangle bounds, bool forSnapshot)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.Clear(bgBase);

        // DshCard (CornerRadius 16, Background DshBgLayer1, Border DshBorderL1)
        var cardRect = new RectangleF(S(14), S(14), bounds.Width - S(28), bounds.Height - S(28));
        using (var cardPath = RoundedRect(cardRect, S(14)))
        using (var cardBrush = new SolidBrush(bgLayer1))
        using (var cardPen = new Pen(borderL1, 1f))
        {
            g.FillPath(cardBrush, cardPath);
            g.DrawPath(cardPen, cardPath);
        }

        // Top-left icon
        var logoRect = new RectangleF(S(28), S(26), S(46), S(46));
        DrawLogo(g, logoRect);

        float textLeft = S(86);
        using (var titleFont = CreateUiFont(13f, FontStyle.Bold))
        using (var monoBadgeFont = CreateMonoFont(8.5f, FontStyle.Bold))
        using (var statusFont = CreateUiFont(9f, FontStyle.Regular))
        using (var stepFont = CreateUiFont(8f, FontStyle.Regular))
        using (var footerFont = CreateUiFont(8f, FontStyle.Regular))
        using (var primaryBrush = new SolidBrush(textPrimary))
        using (var secondaryBrush = new SolidBrush(textSecondary))
        using (var tertiaryBrush = new SolidBrush(textTertiary))
        using (var greenBrush = new SolidBrush(dshGreen))
        {
            g.DrawString(config.Title, titleFont, primaryBrush, new PointF(textLeft, S(24)));
            SizeF titleSize = g.MeasureString(config.Title, titleFont);

            // DshPill version badge
            string badgeText = BuildVersionBadge();
            if (!string.IsNullOrEmpty(badgeText))
            {
                using (var badgeUiFont = CreateUiFont(8.5f, FontStyle.Bold))
                {
                    SizeF badgeSize = g.MeasureString(badgeText, badgeUiFont);
                    var badgeRect = new RectangleF(textLeft + titleSize.Width + S(6), S(26), badgeSize.Width + S(12), S(20));
                    using (var badgePath = RoundedRect(badgeRect, S(7)))
                    using (var bFill = new SolidBrush(accentSoft))
                    using (var bPen = new Pen(borderL2, 1f))
                    using (var bText = new SolidBrush(isDark ? accentEnd : accent))
                    {
                        g.FillPath(bFill, badgePath);
                        g.DrawPath(bPen, badgePath);
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(badgeText, badgeUiFont, bText, badgeRect, sf);
                    }
                }
            }

            // Status indicator dot on top-right
            string guardText = "正在更新";
            SizeF guardSize = g.MeasureString(guardText, stepFont);
            float guardX = bounds.Width - S(28) - guardSize.Width;
            g.FillEllipse(greenBrush, guardX - S(10), S(32), S(6), S(6));
            g.DrawString(guardText, stepFont, tertiaryBrush, new PointF(guardX, S(28)));

            // Stage text
            g.DrawString(StageLabel(), statusFont, secondaryBrush, new PointF(textLeft, S(51)));

            // 3-step pills
            int currentStep = stage <= 1 ? 1 : (stage == 2 ? 2 : 3);
            string[] stepNames = new[] { "1. 解压新版载荷", "2. 替换客户端文件", "3. 启动新版客户端" };
            float stripY = S(84);
            float stripX = S(28);
            for (int i = 0; i < 3; i++)
            {
                int idx = i + 1;
                bool done = idx < currentStep;
                bool active = idx == currentStep;
                string label = (done ? "✓ " : ">_ ") + stepNames[i];
                SizeF sz = g.MeasureString(label, stepFont);
                var pillRect = new RectangleF(stripX, stripY, sz.Width + S(10), S(18));
                if (active)
                {
                    using (var pPath = RoundedRect(pillRect, S(5)))
                    using (var pFill = new SolidBrush(accentSoft))
                        g.FillPath(pFill, pPath);
                }
                using (var sBrush = new SolidBrush(done ? dshGreen : (active ? (isDark ? accentEnd : accent) : textTertiary)))
                {
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(label, stepFont, sBrush, pillRect, sf);
                }
                stripX += pillRect.Width + S(8);
            }

            // Progress bar track
            float barX = S(28);
            float barY = S(112);
            float barW = bounds.Width - S(56);
            float barH = S(6);
            var trackRect = new RectangleF(barX, barY, barW, barH);
            using (var trackPath = RoundedRect(trackRect, barH / 2f))
            using (var trackBrush = new SolidBrush(bgLayer2))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // Progress bar active fill + shimmer
            float fillW = Math.Max(barH * 2f, barW * Math.Max(0.08f, Math.Min(1f, displayProgress)));
            var fillRect = new RectangleF(barX, barY, fillW, barH);
            using (var fillPath = RoundedRect(fillRect, barH / 2f))
            using (var fillBrush = new LinearGradientBrush(new PointF(barX, barY), new PointF(barX + fillW, barY), accent, accentEnd))
            {
                g.FillPath(fillBrush, fillPath);
                float shimmerCenter = barX + fillW * Math.Max(0f, Math.Min(1f, shimmerPhase));
                float shimmerHalf = Math.Max(S(24), fillW * 0.28f);
                using (var oldClip = g.Clip)
                {
                    g.SetClip(fillPath);
                    var shimmerRect = new RectangleF(shimmerCenter - shimmerHalf, barY - 1, shimmerHalf * 2f, barH + 2);
                    using (var shimmerBrush = new LinearGradientBrush(shimmerRect, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(115, 255, 255, 255), LinearGradientMode.Horizontal))
                    {
                        shimmerBrush.InterpolationColors = new ColorBlend(3)
                        {
                            Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                            Positions = new[] { 0f, 0.5f, 1f }
                        };
                        g.FillRectangle(shimmerBrush, shimmerRect);
                    }
                    g.Clip = oldClip;
                }
            }

            // Footer row
            float footerY = S(132);
            g.DrawString("保留已保存的连接、加密凭据与布局预设", footerFont, tertiaryBrush, new PointF(S(28), footerY));

            int secs = (int)elapsed.Elapsed.TotalSeconds;
            if (!string.IsNullOrEmpty(config.ScreenshotPath)) secs = stage <= 1 ? 1 : (stage == 2 ? 3 : 5);
            string rightText = string.Format("STEP {0}/3 · {1:00}:{2:00}", currentStep, secs / 60, secs % 60);
            using (var rightFormat = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(rightText, monoBadgeFont, tertiaryBrush, new PointF(bounds.Width - S(28), footerY), rightFormat);
        }

        // Outer border
        var outerRect = new RectangleF(0.5f, 0.5f, bounds.Width - 1f, bounds.Height - 1f);
        using (var borderPen = new Pen(borderL2, 1f))
        {
            if (dwmRounded && !forSnapshot)
                g.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            else
            {
                using (var borderPath = RoundedRect(outerRect, S(14)))
                    g.DrawPath(borderPen, borderPath);
            }
        }
    }

    private void DrawLogo(Graphics g, RectangleF rect)
    {
        if (logoImage != null)
        {
            g.DrawImage(logoImage, rect);
            return;
        }
        // Fallback vector Harness Portable icon (#4D6BFE squircle with white >_ prompt)
        using (var bgPath = RoundedRect(rect, rect.Width * 0.23f))
        using (var bgBrush = new SolidBrush(Color.FromArgb(77, 107, 254)))
            g.FillPath(bgBrush, bgPath);

        var inner = new RectangleF(rect.X + rect.Width * 0.14f, rect.Y + rect.Height * 0.20f, rect.Width * 0.72f, rect.Height * 0.60f);
        using (var innerPath = RoundedRect(inner, rect.Width * 0.12f))
        using (var framePen = new Pen(Color.White, Math.Max(2f, rect.Width * 0.065f)))
            g.DrawPath(framePen, innerPath);

        using (var promptPen = new Pen(Color.White, Math.Max(2f, rect.Width * 0.075f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLines(promptPen, new[]
            {
                new PointF(rect.X + rect.Width * 0.33f, rect.Y + rect.Height * 0.38f),
                new PointF(rect.X + rect.Width * 0.45f, rect.Y + rect.Height * 0.50f),
                new PointF(rect.X + rect.Width * 0.33f, rect.Y + rect.Height * 0.62f)
            });
            g.DrawLine(promptPen,
                new PointF(rect.X + rect.Width * 0.52f, rect.Y + rect.Height * 0.60f),
                new PointF(rect.X + rect.Width * 0.68f, rect.Y + rect.Height * 0.60f));
        }
    }

    private string BuildVersionBadge()
    {
        if (!string.IsNullOrEmpty(config.FromVersion) && !string.IsNullOrEmpty(config.ToVersion) && config.FromVersion != config.ToVersion)
            return "v" + config.FromVersion + " → v" + config.ToVersion;
        if (!string.IsNullOrEmpty(config.ToVersion))
            return "v" + config.ToVersion;
        return "UPDATE";
    }

    private static Font CreateUiFont(float sizePt, FontStyle style)
    {
        try { return new Font("Microsoft YaHei UI", sizePt, style, GraphicsUnit.Point); }
        catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, sizePt, style, GraphicsUnit.Point); }
    }

    private static Font CreateMonoFont(float sizePt, FontStyle style)
    {
        try { return new Font("Cascadia Mono", sizePt, style, GraphicsUnit.Point); }
        catch { return new Font("Consolas", sizePt, style, GraphicsUnit.Point); }
    }

    private int S(float logical)
    {
        return (int)Math.Round(logical * scale);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(Math.Min(r.Width, r.Height), radius * 2f);
        var path = new GraphicsPath();
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        animTimer.Dispose();
        probeTimer.Dispose();
        if (logoImage != null) logoImage.Dispose();
        base.OnFormClosed(e);
    }
}
