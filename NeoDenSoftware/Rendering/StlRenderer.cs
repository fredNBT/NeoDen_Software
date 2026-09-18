using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Bakes a parsed STL mesh into a single top-down bitmap, rendered once (at upload time in the
/// Footprint Library window) via WPF's built-in 3D system (Viewport3D). From that point on the
/// result is just another footprint image, going through the exact same
/// <see cref="FootprintRenderer.BuildComponentVisual"/> path every uploaded-image footprint
/// already uses - no other part of this app needs to know an STL was ever involved. Assumes the
/// STL's Z axis is "up" (perpendicular to the board) - a model exported with a different up-axis
/// will render from the wrong angle; there's no auto-orientation detection.
/// </summary>
public static class StlRenderer
{
    private static readonly Color BodyColor = Color.FromRgb(60, 60, 64); // neutral IC-package gray

    public static BitmapSource RenderTopDown(StlMesh mesh, int pixelSize = 512)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(BodyColor));
        var model = new GeometryModel3D(BuildGeometry(mesh), material) { BackMaterial = material };

        var modelGroup = new Model3DGroup();
        modelGroup.Children.Add(new AmbientLight(Color.FromRgb(110, 110, 110)));
        // Angled rather than straight down, so a convex top surface still picks up a visible
        // shading gradient even though the camera itself looks straight down - a purely vertical
        // light would flatten every top-down render into one uniform shade regardless of shape.
        modelGroup.Children.Add(new DirectionalLight(Color.FromRgb(200, 200, 200), new Vector3D(-0.4, -0.5, -1)));
        modelGroup.Children.Add(model);

        // Frame the whole mesh's XY extent (with a small margin) under an orthographic top-down
        // camera. The viewport is square, so the camera's Width has to cover whichever of the
        // mesh's X/Y extents is larger, or the shorter axis would get clipped at the frame edges.
        const double marginFactor = 1.08;
        var viewWidth = Math.Max(mesh.SizeX, 0.01) * marginFactor;
        var viewHeight = Math.Max(mesh.SizeY, 0.01) * marginFactor;
        var camera = new OrthographicCamera
        {
            Position = new Point3D(0, 0, Math.Max(mesh.SizeZ, 1) * 4 + 10),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            Width = Math.Max(viewWidth, viewHeight),
        };

        var viewport = new Viewport3D { Width = pixelSize, Height = pixelSize, Camera = camera };
        viewport.Children.Add(new ModelVisual3D { Content = modelGroup });

        // Offscreen render - never added to a window, so layout has to be driven manually before
        // RenderTargetBitmap can capture anything.
        viewport.Measure(new Size(pixelSize, pixelSize));
        viewport.Arrange(new Rect(0, 0, pixelSize, pixelSize));
        viewport.UpdateLayout();

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(viewport);
        target.Freeze();
        return target;
    }

    /// <summary>Builds the mesh geometry centered at the origin (the mesh's own bounding-box
    /// center is subtracted from every vertex) so the STL's original coordinate offset - which
    /// could be anywhere, depending on how the source CAD tool exported it - doesn't throw off
    /// framing under the fixed camera above.</summary>
    private static MeshGeometry3D BuildGeometry(StlMesh mesh)
    {
        var geometry = new MeshGeometry3D();
        var center = mesh.Center;

        foreach (var t in mesh.Triangles)
        {
            var baseIndex = geometry.Positions.Count;
            geometry.Positions.Add(Offset(t.V1, center));
            geometry.Positions.Add(Offset(t.V2, center));
            geometry.Positions.Add(Offset(t.V3, center));
            geometry.Normals.Add(t.Normal);
            geometry.Normals.Add(t.Normal);
            geometry.Normals.Add(t.Normal);
            geometry.TriangleIndices.Add(baseIndex);
            geometry.TriangleIndices.Add(baseIndex + 1);
            geometry.TriangleIndices.Add(baseIndex + 2);
        }

        return geometry;
    }

    private static Point3D Offset(Point3D p, Point3D center) => new(p.X - center.X, p.Y - center.Y, p.Z - center.Z);
}
