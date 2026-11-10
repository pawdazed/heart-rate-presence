// First-run window: explains the two things the user does themselves, then sets the app up.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace HeartRatePresence
{
    class WelcomeForm : Form
    {
        readonly CheckBox chkStartMenu, chkStartup;

        public bool AddStartMenu { get { return chkStartMenu.Checked; } }
        public bool StartWithWindows { get { return chkStartup.Checked; } }

        int S(int px) { return (int)Math.Round(px * DeviceDpi / 96.0); }

        public WelcomeForm()
        {
            Text = AppInfo.Name;
            Font = new Font("Segoe UI", 9.5f);
            Icon = Art.MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.White;
            Padding = new Padding(S(20));

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var pic = new PictureBox { Image = Art.Render(4, S(84)), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, S(18), 0) };
            root.Controls.Add(pic, 0, 0);
            root.SetRowSpan(pic, 6);

            int w = S(420);
            root.Controls.Add(new Label { Text = "Show your heart rate on Discord", AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Margin = new Padding(0, 0, 0, S(10)) }, 1, 0);
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, 0, 0, S(8)),
                Text = "You only do two things yourself:\n\n" +
                       "1.  Install the free HeartCast app on your iPhone. On your watch, open HeartCast and press Start.\n" +
                       "2.  Keep the Discord desktop app open.\n\n" +
                       "This app finds your watch over Bluetooth and keeps your Discord status updated. No account or key needed."
            }, 1, 1);

            var getApp = new LinkLabel { Text = "Get HeartCast on the App Store", AutoSize = true, Margin = new Padding(0, 0, 0, S(14)) };
            getApp.LinkClicked += (s, e) => { try { Process.Start(AppInfo.HeartCastUrl); } catch { } };
            root.Controls.Add(getApp, 1, 2);

            chkStartMenu = new CheckBox { Text = "Add to the Start menu", AutoSize = true, Checked = true };
            chkStartup = new CheckBox { Text = "Open when Windows starts (in the tray)", AutoSize = true, Checked = true, Margin = new Padding(S(3), S(3), S(3), S(16)) };
            root.Controls.Add(chkStartMenu, 1, 3);
            root.Controls.Add(chkStartup, 1, 4);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var ok = new Button { Text = "Set up and start", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(S(12), S(3), S(12), S(3)), Font = new Font("Segoe UI Semibold", 9.5f) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(S(8), S(3), S(8), S(3)) };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 1, 5);

            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
