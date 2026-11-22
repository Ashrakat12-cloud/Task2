using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Newton-Raphson Solver (Windows Forms)
// 1) user types f(x) and x0
// 2) x(i+1) = x(i) - f(x(i)) / f'(x(i)) is repeated until the result is stable
// 3) shows: number of iterations, result of each iteration, graph of f and the points

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

// ---------- Reads a typed function like  x^3 - 8x + 5  ----------
class ExprParser
{
    readonly string s;
    int pos;
    double xv;

    ExprParser(string text) { s = text; }

    public static Func<double, double> Compile(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new Exception("Function is empty");
        new ExprParser(text).Eval(1.0); // test: throws if the syntax is wrong
        return x => new ExprParser(text).Eval(x);
    }

    double Eval(double x)
    {
        xv = x; pos = 0;
        double v = Expr();
        Skip();
        if (pos < s.Length) throw new Exception("Unexpected '" + s[pos] + "'");
        return v;
    }

    void Skip() { while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++; }
    bool Peek(char c) { Skip(); return pos < s.Length && s[pos] == c; }

    double Expr()
    {
        double v = Term();
        while (true)
        {
            if (Peek('+')) { pos++; v += Term(); }
            else if (Peek('-')) { pos++; v -= Term(); }
            else return v;
        }
    }

    double Term()
    {
        double v = Unary();
        while (true)
        {
            if (Peek('*')) { pos++; v *= Unary(); }
            else if (Peek('/')) { pos++; v /= Unary(); }
            else
            {
                Skip();
                // implicit multiplication: 8x, 2(x+1)
                if (pos < s.Length && (char.IsLetter(s[pos]) || s[pos] == '(')) v *= Unary();
                else return v;
            }
        }
    }

    double Unary()
    {
        if (Peek('-')) { pos++; return -Unary(); }
        if (Peek('+')) { pos++; return Unary(); }
        return Power();
    }

    double Power()
    {
        double b = Atom();
        if (Peek('^')) { pos++; double ex = Unary(); return Math.Pow(b, ex); }
        return b;
    }

    double Atom()
    {
        Skip();
        if (pos >= s.Length) throw new Exception("Unexpected end of function");
        char c = s[pos];

        if (char.IsDigit(c) || c == '.')
        {
            int start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
        }
        if (c == '(')
        {
            pos++;
            double v = Expr();
            if (!Peek(')')) throw new Exception("Missing )");
            pos++;
            return v;
        }
        if (char.IsLetter(c))
        {
            int start = pos;
            while (pos < s.Length && char.IsLetter(s[pos])) pos++;
            string name = s.Substring(start, pos - start).ToLowerInvariant();

            if (Peek('('))
            {
                pos++;
                double a = Expr();
                if (!Peek(')')) throw new Exception("Missing )");
                pos++;
                switch (name)
                {
                    case "sin": return Math.Sin(a);
                    case "cos": return Math.Cos(a);
                    case "tan": return Math.Tan(a);
                    case "exp": return Math.Exp(a);
                    case "ln": return Math.Log(a);
                    case "log": return Math.Log10(a);
                    case "sqrt": return Math.Sqrt(a);
                    case "abs": return Math.Abs(a);
                    default: throw new Exception("Unknown function: " + name);
                }
            }
            if (name == "x") return xv;
            if (name == "e") return Math.E;
            if (name == "pi") return Math.PI;
            throw new Exception("Unknown name: " + name);
        }
        throw new Exception("Unexpected '" + c + "'");
    }
}

// ---------- Panel that draws the graph ----------
class PlotPanel : Panel
{
    public Func<double, double> F;
    public List<double> Pts = new List<double>();
    public bool HasRoot;

    public PlotPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    static bool Ok(double v) { return !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 1e6; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (F == null || Pts.Count == 0) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int L = 60, R = 20, T = 20, B = 40;
        int w = Width - L - R, h = Height - T - B;
        if (w < 50 || h < 50) return;

        // x range around the points
        double lo = Pts.Min(), hi = Pts.Max();
        double center = (lo + hi) / 2, half = Math.Max((hi - lo) * 0.8, 2.0);
        double xmin = center - half, xmax = center + half;

        // sample the curve
        int N = 400;
        double[] xsS = new double[N + 1], ysS = new double[N + 1];
        double ymin = 0, ymax = 0;
        for (int i = 0; i <= N; i++)
        {
            double x = xmin + (xmax - xmin) * i / N;
            double y = F(x);
            xsS[i] = x; ysS[i] = y;
            if (Ok(y)) { ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y); }
        }
        foreach (double p in Pts)
        {
            double y = F(p);
            if (Ok(y)) { ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y); }
        }
        if (ymax - ymin < 1e-9) { ymax += 1; ymin -= 1; }
        double padY = (ymax - ymin) * 0.1;
        ymin -= padY; ymax += padY;
        double rng = ymax - ymin;

        Func<double, float> sx = x => (float)(L + (x - xmin) / (xmax - xmin) * w);
        Func<double, float> sy = y => (float)(T + (ymax - y) / (ymax - ymin) * h);

        using (Font font = new Font("Segoe UI", 9))
        using (Pen grid = new Pen(Color.Gainsboro))
        {
            // grid + labels
            for (int k = 0; k <= 5; k++)
            {
                double xv = xmin + (xmax - xmin) * k / 5;
                float px = sx(xv);
                g.DrawLine(grid, px, T, px, T + h);
                g.DrawString(xv.ToString("0.##", CultureInfo.InvariantCulture), font, Brushes.Black, px - 12, T + h + 4);

                double yv = ymin + (ymax - ymin) * k / 5;
                float py = sy(yv);
                g.DrawLine(grid, L, py, L + w, py);
                g.DrawString(yv.ToString("0.##", CultureInfo.InvariantCulture), font, Brushes.Black, 4, py - 8);
            }
            g.DrawRectangle(Pens.Gray, L, T, w, h);

            // axes
            g.DrawLine(Pens.Black, L, sy(0), L + w, sy(0));
            if (xmin < 0 && xmax > 0) g.DrawLine(Pens.Black, sx(0), T, sx(0), T + h);

            g.SetClip(new Rectangle(L, T, w, h));

            // curve
            using (Pen pen = new Pen(Color.RoyalBlue, 2))
            {
                List<PointF> seg = new List<PointF>();
                for (int i = 0; i <= N; i++)
                {
                    double y = ysS[i];
                    bool good = Ok(y) && y > ymin - 3 * rng && y < ymax + 3 * rng;
                    if (good) seg.Add(new PointF(sx(xsS[i]), sy(y)));
                    else
                    {
                        if (seg.Count >= 2) g.DrawLines(pen, seg.ToArray());
                        seg.Clear();
                    }
                }
                if (seg.Count >= 2) g.DrawLines(pen, seg.ToArray());
            }

            // iteration points
            for (int i = 0; i < Pts.Count; i++)
            {
                double y = F(Pts[i]);
                if (!Ok(y)) continue;
                float px = sx(Pts[i]), py = sy(y);
                g.FillEllipse(Brushes.Red, px - 4, py - 4, 8, 8);
                g.DrawString(i.ToString(), font, Brushes.DarkRed, px + 5, py - 16);
            }

            // root
            if (HasRoot)
            {
                double r = Pts[Pts.Count - 1];
                float px = sx(r), py = sy(0);
                g.FillEllipse(Brushes.LimeGreen, px - 6, py - 6, 12, 12);
                g.DrawString("root = " + r.ToString("0.#####", CultureInfo.InvariantCulture),
                    font, Brushes.DarkGreen, px + 8, py + 6);
            }
            g.ResetClip();
        }
    }
}

// ---------- Main window ----------
class MainForm : Form
{
    TextBox txtF = new TextBox();
    TextBox txtX0 = new TextBox();
    TextBox txtTol = new TextBox();
    TextBox txtOut = new TextBox();
    Button btn = new Button();
    PlotPanel plot = new PlotPanel();

    public MainForm()
    {
        Text = "Newton-Raphson Solver";
        Width = 1100; Height = 650;

        FlowLayoutPanel top = new FlowLayoutPanel();
        top.Dock = DockStyle.Top;
        top.Height = 45;
        top.Padding = new Padding(5);

        txtF.Width = 260; txtF.Text = "x^3 - 8*x + 5";
        txtX0.Width = 60; txtX0.Text = "0";
        txtTol.Width = 80; txtTol.Text = "0.0001";
        btn.Text = "Solve"; btn.Width = 80;
        btn.Click += (s, e) => Run();
        AcceptButton = btn;

        top.Controls.Add(new Label { Text = "f(x) =", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        top.Controls.Add(txtF);
        top.Controls.Add(new Label { Text = "x0 =", AutoSize = true, Margin = new Padding(10, 8, 3, 0) });
        top.Controls.Add(txtX0);
        top.Controls.Add(new Label { Text = "error <", AutoSize = true, Margin = new Padding(10, 8, 3, 0) });
        top.Controls.Add(txtTol);
        top.Controls.Add(btn);

        txtOut.Multiline = true;
        txtOut.ReadOnly = true;
        txtOut.ScrollBars = ScrollBars.Vertical;
        txtOut.Dock = DockStyle.Left;
        txtOut.Width = 430;
        txtOut.Font = new Font("Consolas", 10);

        plot.Dock = DockStyle.Fill;
        plot.BackColor = Color.White;

        Controls.Add(plot);
        Controls.Add(txtOut);
        Controls.Add(top);

        Shown += (s, e) => Run();
    }

    static double ParseNum(string t)
    {
        return double.Parse(t.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static string Fmt(double v) { return v.ToString("F6", CultureInfo.InvariantCulture); }

    void Run()
    {
        Func<double, double> f;
        double x0, tol;
        try
        {
            f = ExprParser.Compile(txtF.Text);
            x0 = ParseNum(txtX0.Text);
            tol = ParseNum(txtTol.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Input error: " + ex.Message);
            return;
        }

        // derivative (numerical)
        Func<double, double> df = x =>
        {
            double h = 1e-6 * Math.Max(1.0, Math.Abs(x));
            return (f(x + h) - f(x - h)) / (2 * h);
        };

        List<double> xs = new List<double> { x0 };
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("f(x) = " + txtF.Text);
        sb.AppendLine("x0 = " + Fmt(x0));
        sb.AppendLine();
        sb.AppendLine(string.Format("{0,-4}{1,-14}{2,-14}{3}", "i", "x_i", "f(x_i)", "|x(i+1)-x(i)|"));
        sb.AppendLine(string.Format("{0,-4}{1,-14}{2,-14}{3}", 0, Fmt(x0), Fmt(f(x0)), "----"));

        double x = x0;
        bool ok = false;
        for (int n = 1; n <= 100; n++)
        {
            double d = df(x);
            if (d == 0 || double.IsNaN(d) || double.IsInfinity(d))
            {
                sb.AppendLine("f'(x) is zero or invalid. Try another x0.");
                break;
            }
            double xn = x - f(x) / d;
            if (double.IsNaN(xn) || double.IsInfinity(xn))
            {
                sb.AppendLine("The value blew up. Try another x0.");
                break;
            }
            double err = Math.Abs(xn - x);
            xs.Add(xn);
            sb.AppendLine(string.Format("{0,-4}{1,-14}{2,-14}{3}", n, Fmt(xn), Fmt(f(xn)), Fmt(err)));
            x = xn;
            if (err < tol) { ok = true; break; }
        }

        sb.AppendLine();
        sb.AppendLine("Number of iterations: " + (xs.Count - 1));
        if (ok)
        {
            sb.AppendLine("Root = " + Fmt(x));
            sb.AppendLine("f(root) = " + Fmt(f(x)));
        }
        else sb.AppendLine("No convergence.");

        txtOut.Text = sb.ToString();

        plot.F = f;
        plot.Pts = xs;
        plot.HasRoot = ok;
        plot.Invalidate();
    }
}
