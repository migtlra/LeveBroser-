using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeveBrowser
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    class Tab
    {
        public WebView2 View;
        public Button Btn;
    }

    class MainForm : Form
    {
        const int MaxTabs = 3;
        const string Home = "https://www.google.com";

        // Argumentos do Chromium para economizar RAM/CPU
        const string Args =
            "--renderer-process-limit=2 --in-process-gpu --disable-extensions " +
            "--disable-background-networking --disable-sync --disable-component-update " +
            "--disable-breakpad --disable-smooth-scrolling " +
            "--disable-features=Translate,MediaRouter,OptimizationHints,AutofillServerCommunication " +
            "--js-flags=--max-old-space-size=192 --disk-cache-size=33554432";

        static readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "adservice.google.com", "google-analytics.com", "adnxs.com", "taboola.com",
            "outbrain.com", "criteo.com", "criteo.net", "scorecardresearch.com",
            "pubmatic.com", "rubiconproject.com", "amazon-adsystem.com", "adsrvr.org",
            "moatads.com", "hotjar.com", "ads.yahoo.com", "quantserve.com", "2mdn.net"
        };

        readonly List<Tab> tabs = new List<Tab>();
        Tab current;
        CoreWebView2Environment env;

        readonly Panel tabBar = new Panel { Dock = DockStyle.Top, Height = 30 };
        readonly Panel toolBar = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4, 5, 4, 4) };
        readonly Panel host = new Panel { Dock = DockStyle.Fill };
        readonly TextBox address = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };

        public MainForm()
        {
            Text = "Leve Browser";
            Width = 1000;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;

            var back = MakeBtn("<", DockStyle.Left, 34);
            var fwd = MakeBtn(">", DockStyle.Left, 34);
            var reload = MakeBtn("R", DockStyle.Left, 34);
            var plus = MakeBtn("+", DockStyle.Right, 34);
            var close = MakeBtn("x", DockStyle.Right, 34);

            back.Click += (s, e) => { var c = Core(); if (c != null && c.CanGoBack) c.GoBack(); };
            fwd.Click += (s, e) => { var c = Core(); if (c != null && c.CanGoForward) c.GoForward(); };
            reload.Click += (s, e) => { var c = Core(); if (c != null) c.Reload(); };
            plus.Click += (s, e) => AddTab(Home);
            close.Click += (s, e) => CloseCurrent();

            address.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(address.Text); }
            };

            // ordem de adicao importa para o Dock
            toolBar.Controls.Add(address);
            toolBar.Controls.Add(plus);
            toolBar.Controls.Add(close);
            toolBar.Controls.Add(reload);
            toolBar.Controls.Add(fwd);
            toolBar.Controls.Add(back);

            Controls.Add(host);
            Controls.Add(toolBar);
            Controls.Add(tabBar);
        }

        static Button MakeBtn(string text, DockStyle dock, int width)
        {
            return new Button { Text = text, Dock = dock, Width = width, FlatStyle = FlatStyle.Flat };
        }

        CoreWebView2 Core()
        {
            return current != null ? current.View.CoreWebView2 : null;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeveBrowser");
                var opt = new CoreWebView2EnvironmentOptions(Args);
                env = await CoreWebView2Environment.CreateAsync(null, dir, opt);
                AddTab(Home);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nao foi possivel iniciar o WebView2.\nInstale o WebView2 Runtime " +
                    "(versao x86 se o Windows for 32 bits).\n\n" + ex.Message);
                Close();
            }
        }

        async void AddTab(string url)
        {
            if (env == null) return;
            if (tabs.Count >= MaxTabs)
            {
                MessageBox.Show("Limite de " + MaxTabs + " abas (para economizar memoria).");
                return;
            }

            var t = new Tab();
            t.View = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            t.Btn = new Button
            {
                Text = "Nova aba",
                Width = 160,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft
            };
            t.Btn.Click += (s, e) => Select(t);
            t.Btn.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Middle) { Select(t); CloseCurrent(); }
            };

            host.Controls.Add(t.View);
            tabs.Add(t);
            Select(t);

            try
            {
                await t.View.EnsureCoreWebView2Async(env);
                var core = t.View.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;

                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnRequest;

                core.NewWindowRequested += (s, e) => { e.Handled = true; core.Navigate(e.Uri); };
                core.DocumentTitleChanged += (s, e) =>
                {
                    string title = core.DocumentTitle ?? "";
                    t.Btn.Text = title.Length > 20 ? title.Substring(0, 20) + "..." : title;
                };
                core.SourceChanged += (s, e) =>
                {
                    if (current == t) address.Text = core.Source;
                };

                core.Navigate(url);
            }
            catch { }
        }

        void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            Uri u;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out u)) return;
            string h = u.Host;
            while (h.IndexOf('.') >= 0)
            {
                if (Blocked.Contains(h))
                {
                    e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
                    return;
                }
                h = h.Substring(h.IndexOf('.') + 1);
            }
        }

        void Select(Tab t)
        {
            foreach (var o in tabs)
            {
                if (o != t && o.View.Visible)
                {
                    o.View.Visible = false;
                    Park(o);
                }
            }
            current = t;
            t.View.Visible = true;

            var core = t.View.CoreWebView2;
            if (core != null)
            {
                try
                {
                    if (core.IsSuspended) core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                catch { }
                address.Text = core.Source;
            }
            RefreshTabBar();
        }

        // Aba em segundo plano: suspende e pede menos memoria
        async void Park(Tab o)
        {
            try
            {
                var c = o.View.CoreWebView2;
                if (c == null) return;
                c.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                await c.TrySuspendAsync();
            }
            catch { }
        }

        void CloseCurrent()
        {
            if (current == null) return;
            var t = current;
            int i = tabs.IndexOf(t);
            tabs.Remove(t);
            host.Controls.Remove(t.View);
            t.View.Dispose();
            current = null;
            if (tabs.Count == 0) { RefreshTabBar(); AddTab(Home); return; }
            Select(tabs[Math.Max(0, i - 1)]);
        }

        void RefreshTabBar()
        {
            tabBar.Controls.Clear();
            for (int i = tabs.Count - 1; i >= 0; i--)
            {
                tabs[i].Btn.BackColor = tabs[i] == current ? Color.White : SystemColors.Control;
                tabBar.Controls.Add(tabs[i].Btn);
            }
        }

        void Go(string text)
        {
            var c = Core();
            if (c == null) return;
            text = text.Trim();
            if (text.Length == 0) return;
            string url;
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = text;
            else if (text.IndexOf(' ') < 0 && text.IndexOf('.') > 0)
                url = "https://" + text;
            else
                url = "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
            c.Navigate(url);
        }
    }
}
