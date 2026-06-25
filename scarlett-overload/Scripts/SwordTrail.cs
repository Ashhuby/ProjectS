using Godot;
using System.Collections.Generic;

public partial class SwordTrail : MeshInstance3D
{
    [Export] public int MaxPoints = 14;
    [Export] public Color TipColor = new Color(1f, 0.95f, 0.8f, 0.95f);
    [Export] public Color BaseColor = new Color(1f, 0.6f, 0.2f, 0.5f);
    [Export] public float Jitter = 0.025f;

    private bool _emitting;
    private Node3D _swordNode;
    private Vector3 _tipOffset;
    private ImmediateMesh _mesh;
    private StandardMaterial3D _material;
    private readonly List<TrailPoint> _points = new();

    private struct TrailPoint
    {
        public Vector3 Base;
        public Vector3 Tip;
    }

    public void Initialize(Node3D swordNode, Vector3 tipOffset)
    {
        _swordNode = swordNode;
        _tipOffset = tipOffset;
    }

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;

        _mesh = new ImmediateMesh();
        Mesh = _mesh;

        _material = new StandardMaterial3D();
        _material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _material.VertexColorUseAsAlbedo = true;
        _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
    }

    public void StartEmitting()
    {
        _emitting = true;
        _points.Clear();
    }

    public void StopEmitting()
    {
        _emitting = false;
    }

    public override void _Process(double delta)
    {
        if (_swordNode == null || !IsInstanceValid(_swordNode))
        {
            _mesh.ClearSurfaces();
            return;
        }

        if (_emitting)
        {
            var xform = _swordNode.GlobalTransform;
            var basePos = xform.Origin;
            var tipPos = xform.Origin + xform.Basis * _tipOffset;

            // Slight jitter breaks the clean CG look
            if (Jitter > 0f)
            {
                var noise = new Vector3(
                    (float)GD.RandRange(-Jitter, Jitter),
                    (float)GD.RandRange(-Jitter, Jitter),
                    (float)GD.RandRange(-Jitter, Jitter)
                );
                tipPos += noise;
                basePos += noise * 0.5f;
            }

            _points.Add(new TrailPoint { Base = basePos, Tip = tipPos });

            while (_points.Count > MaxPoints)
                _points.RemoveAt(0);
        }
        else if (_points.Count > 0)
        {
            _points.RemoveAt(0);
        }

        RebuildMesh();
    }

    private void RebuildMesh()
    {
        _mesh.ClearSurfaces();
        if (_points.Count < 2) return;

        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _material);

        for (int i = 0; i < _points.Count; i++)
        {
            // 0 = oldest, 1 = newest
            float t = (float)i / (_points.Count - 1);

            // Squared falloff — front is bright, tail drops off fast
            float alpha = t * t;

            // Tip edge: bright, white-hot
            var tipCol = new Color(TipColor.R, TipColor.G, TipColor.B, alpha * TipColor.A);

            // Base edge: dimmer, more orange — gives a gradient across the blade width
            var baseCol = new Color(BaseColor.R, BaseColor.G, BaseColor.B, alpha * BaseColor.A);

            _mesh.SurfaceSetColor(tipCol);
            _mesh.SurfaceAddVertex(_points[i].Tip);

            _mesh.SurfaceSetColor(baseCol);
            _mesh.SurfaceAddVertex(_points[i].Base);
        }

        _mesh.SurfaceEnd();
    }
}
