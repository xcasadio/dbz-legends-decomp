using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

/// <summary>
/// 3D wireframe viewer for STGxMD.B stage mesh files.
///
/// Controls:
///   Left-drag   — rotate (yaw + pitch)
///   Right-drag  — pan
///   Scroll      — zoom
///   R key       — reset view
/// </summary>
public class STG_MD_Control : AnalyserControl
{
    // ── Model ────────────────────────────────────────────────────────────────
    private StgModelFile? _model;
    private StgTriangle[] _worldTris = [];

    // Pre-computed world AABB for auto-scale
    private float _sceneScale = 1f;
    private Vec3  _sceneCenter;

    // ── Camera state ─────────────────────────────────────────────────────────
    private float _yaw   = 0.4f;   // radians
    private float _pitch = 0.3f;   // radians
    private float _zoom  = 1.0f;
    private float _panX  = 0f;
    private float _panY  = 0f;

    // ── Mouse ─────────────────────────────────────────────────────────────────
    private Point _lastMouse;
    private bool  _leftDown;
    private bool  _rightDown;

    // ── UI ────────────────────────────────────────────────────────────────────
    private Panel    _viewport  = null!;
    private Label    _statsLabel = null!;
    private CheckBox _chkSolid  = null!;

    // ─────────────────────────────────────────────────────────────────────────

    public STG_MD_Control()
    {
        InitializeComponent();
    }

    public override void Initialize(string filePath)
    {
        _model = StgMdLoader.Load(filePath);
        _worldTris = _model.GetWorldTriangles().ToArray();
        ComputeSceneTransform();
        UpdateStats(filePath);
        _viewport.Invalidate();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        _viewport   = new DoubleBufferPanel();
        _statsLabel = new Label();
        _chkSolid   = new CheckBox();

        SuspendLayout();

        _viewport.Dock        = DockStyle.Fill;
        _viewport.BackColor   = Color.FromArgb(18, 20, 28);
        _viewport.Paint      += Viewport_Paint;
        _viewport.MouseDown  += Viewport_MouseDown;
        _viewport.MouseUp    += Viewport_MouseUp;
        _viewport.MouseMove  += Viewport_MouseMove;
        _viewport.MouseWheel += Viewport_MouseWheel;
        _viewport.KeyDown    += Viewport_KeyDown;
        _viewport.TabStop     = true;

        _statsLabel.Dock      = DockStyle.Bottom;
        _statsLabel.Height    = 20;
        _statsLabel.ForeColor = Color.Silver;
        _statsLabel.BackColor = Color.FromArgb(30, 30, 40);
        _statsLabel.Font      = new Font("Consolas", 8f);
        _statsLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statsLabel.Padding   = new Padding(4, 0, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock      = DockStyle.Bottom,
            Height    = 26,
            BackColor = Color.FromArgb(35, 38, 50)
        };

        _chkSolid.Text      = "Colorize by type";
        _chkSolid.ForeColor = Color.Silver;
        _chkSolid.Checked   = false;
        _chkSolid.CheckedChanged += (_, _) => _viewport.Invalidate();
        toolbar.Controls.Add(_chkSolid);

        var btnReset = new Button { Text = "Reset view", Width = 80, Height = 22 };
        btnReset.Click += (_, _) => ResetView();
        toolbar.Controls.Add(btnReset);

        Controls.Add(_viewport);
        Controls.Add(toolbar);
        Controls.Add(_statsLabel);

        ResumeLayout(false);
    }

    // ── Auto-scale / centering ───────────────────────────────────────────────

    private void ComputeSceneTransform()
    {
        if (_worldTris.Length == 0) { _sceneScale = 1f; _sceneCenter = default; return; }

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (var t in _worldTris)
        {
            foreach (var v in new[] { t.V0, t.V1, t.V2 })
            {
                if (v.X < minX) minX = v.X;  if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;  if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z;  if (v.Z > maxZ) maxZ = v.Z;
            }
        }

        _sceneCenter = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        _sceneScale  = extent > 0 ? 400f / extent : 1f;
    }

    private void ResetView()
    {
        _yaw = 0.4f; _pitch = 0.3f; _zoom = 1f; _panX = 0f; _panY = 0f;
        _viewport.Invalidate();
    }

    private void UpdateStats(string filePath)
    {
        int meshCount  = _model?.MeshEntries.Count(m => m.FileOffset > 0) ?? 0;
        int partCount  = _model?.Particles.Count ?? 0;
        int triCount   = _worldTris.Length;
        _statsLabel.Text =
            $"  {Path.GetFileName(filePath)}   |   {meshCount} meshes   " +
            $"|   {partCount} particles   |   {triCount:N0} triangles   " +
            $"|   Drag: rotate   RightDrag: pan   Scroll: zoom   R: reset";
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Viewport_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (_worldTris.Length == 0)
        {
            g.DrawString("No model loaded or no triangles parsed.",
                         Font, Brushes.Gray, 10, 10);
            return;
        }

        int    cx    = _viewport.Width  / 2 + (int)_panX;
        int    cy    = _viewport.Height / 2 + (int)_panY;
        float  fov   = Math.Min(_viewport.Width, _viewport.Height) * 0.45f * _zoom;

        // Build rotation matrix (yaw then pitch)
        float sy = MathF.Sin(_yaw),   cy2 = MathF.Cos(_yaw);
        float sp = MathF.Sin(_pitch),  cp  = MathF.Cos(_pitch);

        // Collect projected triangles with depth for back-to-front sort
        var projected = new List<(PointF p0, PointF p1, PointF p2, float depth, Color c)>(
            _worldTris.Length);

        bool colorByType = _chkSolid.Checked;

        foreach (var tri in _worldTris)
        {
            var (px0, py0, pz0) = Project(tri.V0, sy, cy2, sp, cp);
            var (px1, py1, pz1) = Project(tri.V1, sy, cy2, sp, cp);
            var (px2, py2, pz2) = Project(tri.V2, sy, cy2, sp, cp);

            // Simple back-face cull via signed area in screen space
            float area = (px1 - px0) * (py2 - py0) - (px2 - px0) * (py1 - py0);
            if (area > 0) continue;  // back-facing

            float depth = (pz0 + pz1 + pz2) / 3f;

            Color wireColor = colorByType
                ? tri.AverageColor
                : Color.FromArgb(60, 160, 240);   // default cyan-blue wireframe

            projected.Add((
                new PointF(cx + px0 * fov / (pz0 + 600f), cy - py0 * fov / (pz0 + 600f)),
                new PointF(cx + px1 * fov / (pz1 + 600f), cy - py1 * fov / (pz1 + 600f)),
                new PointF(cx + px2 * fov / (pz2 + 600f), cy - py2 * fov / (pz2 + 600f)),
                depth, wireColor));
        }

        // Sort back-to-front (painter's algorithm)
        projected.Sort((a, b) => b.depth.CompareTo(a.depth));

        // Draw wireframe
        using var defaultPen = new Pen(Color.FromArgb(60, 160, 240), 0.8f);
        foreach (var (p0, p1, p2, _, color) in projected)
        {
            var pen = colorByType ? new Pen(Color.FromArgb(180, color), 0.8f) : defaultPen;
            g.DrawLine(pen, p0, p1);
            g.DrawLine(pen, p1, p2);
            g.DrawLine(pen, p2, p0);
            if (colorByType) pen.Dispose();
        }

        // Axes helper (bottom-left corner)
        DrawAxes(g, sy, cy2, sp, cp);
    }

    /// <summary>Projects a world Vec3 into camera space. Returns (camX, camY, camZ).</summary>
    private (float x, float y, float z) Project(Vec3 v, float sy, float cy, float sp, float cp)
    {
        // Center and scale
        float lx = (v.X - _sceneCenter.X) * _sceneScale;
        float ly = (v.Y - _sceneCenter.Y) * _sceneScale;
        float lz = (v.Z - _sceneCenter.Z) * _sceneScale;

        // Rotate: yaw around Y, then pitch around X
        float rx  =  cy * lx + sy * lz;
        float ry1 = -sy * lx + cy * lz;
        float ry  =  cp * ly - sp * ry1;
        float rz  =  sp * ly + cp * ry1;

        return (rx, ry, rz);
    }

    private void DrawAxes(Graphics g, float sy, float cy, float sp, float cp)
    {
        const int ox = 50, oy_base = 50;
        int oy = _viewport.Height - oy_base;
        const float len = 30f;

        void DrawAxis(Vec3 dir, Color color, string label)
        {
            var (ex, ey, ez) = Project(dir, sy, cy, sp, cp);
            float fov  = len;
            float denom = ez + 600f;
            var ep = new PointF(ox + ex * fov / denom, oy - ey * fov / denom);
            using var pen   = new Pen(color, 2f);
            using var brush = new SolidBrush(color);
            g.DrawLine(pen, ox, oy, ep.X, ep.Y);
            g.DrawString(label, new Font("Arial", 7f), brush, ep.X, ep.Y);
        }

        DrawAxis(new Vec3(1, 0, 0), Color.Red,   "X");
        DrawAxis(new Vec3(0, 1, 0), Color.LimeGreen, "Y");
        DrawAxis(new Vec3(0, 0, 1), Color.DodgerBlue, "Z");
    }

    // ── Mouse events ─────────────────────────────────────────────────────────

    private void Viewport_MouseDown(object? sender, MouseEventArgs e)
    {
        _viewport.Focus();
        _lastMouse = e.Location;
        if (e.Button == MouseButtons.Left)  _leftDown  = true;
        if (e.Button == MouseButtons.Right) _rightDown = true;
    }

    private void Viewport_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)  _leftDown  = false;
        if (e.Button == MouseButtons.Right) _rightDown = false;
    }

    private void Viewport_MouseMove(object? sender, MouseEventArgs e)
    {
        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;

        if (_leftDown)
        {
            _yaw   += dx * 0.008f;
            _pitch += dy * 0.008f;
            _pitch  = Math.Clamp(_pitch, -MathF.PI / 2 + 0.05f, MathF.PI / 2 - 0.05f);
            _viewport.Invalidate();
        }
        else if (_rightDown)
        {
            _panX += dx;
            _panY += dy;
            _viewport.Invalidate();
        }

        _lastMouse = e.Location;
    }

    private void Viewport_MouseWheel(object? sender, MouseEventArgs e)
    {
        _zoom *= e.Delta > 0 ? 1.12f : 0.89f;
        _zoom  = Math.Clamp(_zoom, 0.05f, 50f);
        _viewport.Invalidate();
    }

    private void Viewport_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.R) ResetView();
    }

    // ── Helper: double-buffered panel ─────────────────────────────────────────

    private sealed class DoubleBufferPanel : Panel
    {
        public DoubleBufferPanel() { DoubleBuffered = true; }
    }
}
