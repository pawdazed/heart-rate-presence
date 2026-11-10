// Light / dark themes, plus the custom tab bar (Windows' own TabControl can't be colored dark).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HeartRatePresence
{
    class Theme
    {
        public bool Dark;
        public Color Bg, Alt, Surface, Text, Muted, Border, Button, ButtonHover, Accent, Selection, Card, CardBorder;

        public static readonly Theme Light = new Theme
        {
            Dark = false,
            Bg = Color.White,
            Alt = Color.FromArgb(0xF2, 0xF3, 0xF5),
            Surface = Color.White,
            Text = Color.FromArgb(0x1F, 0x23, 0x28),
            Muted = Color.FromArgb(0x6B, 0x72, 0x80),
            Border = Color.FromArgb(0xD0, 0xD4, 0xDA),
            Button = Color.FromArgb(0xF2, 0xF3, 0xF5),
            ButtonHover = Color.FromArgb(0xE3, 0xE5, 0xE8),
            Accent = Color.FromArgb(0x58, 0x65, 0xF2),
            Selection = Color.FromArgb(0xDD, 0xE1, 0xFF),
            Card = Color.FromArgb(0x23, 0x24, 0x28),
            CardBorder = Color.FromArgb(0x23, 0x24, 0x28),
        };

        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true,
            Bg = Color.FromArgb(0x2B, 0x2D, 0x31),
            Alt = Color.FromArgb(0x23, 0x24, 0x28),
            Surface = Color.FromArgb(0x1E, 0x1F, 0x22),
            Text = Color.FromArgb(0xF2, 0xF3, 0xF5),
            Muted = Color.FromArgb(0xA0, 0xA6, 0xAE),
            Border = Color.FromArgb(0x3F, 0x41, 0x47),
            Button = Color.FromArgb(0x38, 0x3A, 0x40),
            ButtonHover = Color.FromArgb(0x45, 0x47, 0x4E),
            Accent = Color.FromArgb(0x7A, 0x85, 0xFF),
            Selection = Color.FromArgb(0x3C, 0x42, 0x70),
            Card = Color.FromArgb(0x1E, 0x1F, 0x22),
            CardBorder = Color.FromArgb(0x3F, 0x41, 0x47),
        };

        public static Theme Current = Light;

        public static readonly string[] PrefKeys = { "System", "Light", "Dark" };
        public static readonly string[] PrefNames = { "Match Windows", "Light", "Dark" };

        public static bool WindowsPrefersDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static Theme Resolve(string pref)
        {
            if (pref == "Dark") return DarkTheme;
            if (pref == "Light") return Light;
            return WindowsPrefersDark() ? DarkTheme : Light;
        }

        // ---- native bits: dark title bar and dark scrollbars (Windows 10 1809+; silently ignored elsewhere)

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public static void TitleBar(Form f, bool dark)
        {
            if (!f.IsHandleCreated) return;
            int v = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED
            }
            catch { }
        }

        static void Scrollbars(Control c, bool dark) { Native(c, dark ? "DarkMode_Explorer" : "Explorer"); }

        static void Native(Control c, string themeName)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, themeName, null); c.Invalidate(); } catch { }
        }

        /// <summary>Colors a control tree. Containers and labels inherit BackColor/ForeColor from the form.</summary>
        public static void Apply(Control c, Theme t)
        {
            if (c is TextBox)
            {
                var tb = (TextBox)c;
                tb.BackColor = t.Surface; tb.ForeColor = t.Text; tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is NumericUpDown)
            {
                var n = (NumericUpDown)c;
                n.BackColor = t.Surface; n.ForeColor = t.Text; n.BorderStyle = BorderStyle.FixedSingle;
                foreach (Control part in n.Controls) Native(part, t.Dark ? "DarkMode_Explorer" : "Explorer");
                return;
            }
            else if (c is ComboBox)
            {
                // Windows' own dark combo box style (same one Explorer's dialogs use)
                var cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Standard;
                cb.BackColor = t.Surface; cb.ForeColor = t.Text;
                Native(cb, t.Dark ? "DarkMode_CFD" : "Explorer");
                return;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = t.Button; b.ForeColor = t.Text;
                b.FlatAppearance.BorderColor = t.Border;
                b.FlatAppearance.MouseOverBackColor = t.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = t.Border;
            }
            else if (c is DataGridView)
            {
                var g = (DataGridView)c;
                g.EnableHeadersVisualStyles = false;
                g.BackgroundColor = t.Surface;
                g.GridColor = t.Border;
                g.DefaultCellStyle.BackColor = t.Surface;
                g.DefaultCellStyle.ForeColor = t.Text;
                g.DefaultCellStyle.SelectionBackColor = t.Selection;
                g.DefaultCellStyle.SelectionForeColor = t.Text;
                g.ColumnHeadersDefaultCellStyle.BackColor = t.Alt;
                g.ColumnHeadersDefaultCellStyle.ForeColor = t.Text;
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.Alt;
                g.ColumnHeadersDefaultCellStyle.SelectionForeColor = t.Text;
                g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                foreach (Control sb in g.Controls) Scrollbars(sb, t.Dark);
            }
            else if (c is TrackBar)
            {
                c.BackColor = t.Bg;
            }
            else if (c is LinkLabel)
            {
                var ll = (LinkLabel)c;
                ll.LinkColor = t.Accent; ll.ActiveLinkColor = t.Accent; ll.VisitedLinkColor = t.Accent;
            }
            else if (c is Label && "muted".Equals(c.Tag))
            {
                c.ForeColor = t.Muted;
            }

            var sc = c as ScrollableControl;
            if (sc != null && sc.AutoScroll) Scrollbars(sc, t.Dark);

            foreach (Control child in c.Controls)
                if (!(c is DataGridView)) Apply(child, t);
        }
    }

    /// <summary>Colors right-click menus (the tray menu) to match the theme.</summary>
    class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer(Theme t) : base(new Colors(t)) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Current.Text;
            base.OnRenderItemText(e);
        }

        class Colors : ProfessionalColorTable
        {
            readonly Theme t;
            public Colors(Theme theme) { t = theme; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return t.Alt; } }
            public override Color ImageMarginGradientBegin { get { return t.Alt; } }
            public override Color ImageMarginGradientMiddle { get { return t.Alt; } }
            public override Color ImageMarginGradientEnd { get { return t.Alt; } }
            public override Color MenuBorder { get { return t.Border; } }
            public override Color MenuItemBorder { get { return t.ButtonHover; } }
            public override Color MenuItemSelected { get { return t.ButtonHover; } }
            public override Color SeparatorDark { get { return t.Border; } }
            public override Color SeparatorLight { get { return t.Border; } }
        }
    }

    /// <summary>A page of the main window (stands in for a TabPage).</summary>
    class Page : Panel
    {
        public Page(string title) { Text = title; Dock = DockStyle.Fill; }
        public bool UseVisualStyleBackColor { get; set; } // kept so page setup code reads the same as before
    }

    /// <summary>Flat tab strip: titles in a row, the selected one underlined in the accent color.</summary>
    class NavBar : Control
    {
        readonly List<string> titles = new List<string>();
        readonly List<Rectangle> hit = new List<Rectangle>();
        int selected, hover = -1;
        public event Action<int> SelectedChanged;

        public NavBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = new Font("Segoe UI Semibold", 10f);
            Cursor = Cursors.Hand;
        }

        public void Add(string title) { titles.Add(title); Invalidate(); }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); }
        }

        int S(int px) { return (int)Math.Round(px * DeviceDpi / 96.0); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.Clear(t.Alt);
            using (var line = new SolidBrush(t.Border)) g.FillRectangle(line, 0, Height - 1, Width, 1);
            hit.Clear();
            int x = S(14);
            for (int i = 0; i < titles.Count; i++)
            {
                var size = TextRenderer.MeasureText(titles[i], Font);
                var r = new Rectangle(x, 0, size.Width + S(16), Height);
                hit.Add(r);
                var color = i == selected || i == hover ? t.Text : t.Muted;
                TextRenderer.DrawText(g, titles[i], Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (i == selected)
                    using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, r.X + S(6), Height - S(3), r.Width - S(12), S(3));
                x = r.Right + S(4);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < hit.Count; i++) if (hit[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && h != selected)
            {
                SelectedIndex = h;
                if (SelectedChanged != null) SelectedChanged(h);
            }
            base.OnMouseClick(e);
        }
    }
}
