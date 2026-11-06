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
