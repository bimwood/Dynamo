﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

using Dynamo.DSEngine;
using Dynamo.ViewModels;
using HelixToolkit.Wpf;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX.Model.Geometry;

using SharpDX;

using Color = System.Windows.Media.Color;
using Material = System.Windows.Media.Media3D.Material;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Point = System.Windows.Point;

namespace Dynamo.Controls
{
    /// <summary>
    /// Interaction logic for WatchControl.xaml
    /// </summary>
    public partial class Watch3DView : UserControl, INotifyPropertyChanged
    {
        #region events

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #endregion

        #region private members

        private readonly Guid _id=Guid.Empty;
        private Point _rightMousePoint;
        private LineGeometry3D _grid;
        private RenderTechnique renderTechnique;
        private HelixToolkit.Wpf.SharpDX.Camera camera;

        #endregion

        #region public properties

        public Material MeshMaterial
        {
            get { return Materials.White; }
        }

        public LineGeometry3D Grid
        {
            get { return _grid; }
            set
            {
                _grid = value;
                NotifyPropertyChanged("Grid");
            }
        }

        public PointGeometry3D Points { get; set; }

        public PointGeometry3D PointsSelected { get; set; }

        public LineGeometry3D Lines { get; set; }

        public LineGeometry3D XAxes { get; set; }

        public LineGeometry3D YAxes { get; set; }

        public LineGeometry3D ZAxes { get; set; }

        public MeshGeometry3D Mesh { get; set; }

        public LineGeometry3D LinesSelected { get; set; }

        public MeshGeometry3D MeshSelected { get; set; }

        //public List<BillboardTextItem> Text { get; set; }

        public Viewport3DX View
        {
            get { return watch_view; }
        }

        /// <summary>
        /// Used for testing to track the number of meshes that are merged
        /// during render.
        /// </summary>
        public int MeshCount { get; set; }

        public PhongMaterial RedMaterial { get; private set; }
        public PhongMaterial CyanMaterial { get; private set; }
        public Vector3 DirectionalLightDirection { get; private set; }
        public Color4 DirectionalLightColor { get; private set; }
        public Color4 AmbientLightColor { get; private set; }
        public System.Windows.Media.Media3D.Transform3D Model1Transform { get; private set; }
        public RenderTechnique RenderTechnique
        {
            get
            {
                return this.renderTechnique;
            }
            set
            {
                renderTechnique = value;
                NotifyPropertyChanged("RenderTechnique");
            }
        }

        public HelixToolkit.Wpf.SharpDX.Camera Camera
        {
            get
            {
                return this.camera;
            }

            protected set
            {
                camera = value;
                NotifyPropertyChanged("Camera");
            }
        }

        #endregion

        #region constructors

        public Watch3DView()
        {
            SetupScene();

            InitializeComponent();
            watch_view.DataContext = this;
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        public Watch3DView(Guid id)
        {
            SetupScene();

            InitializeComponent();
            watch_view.DataContext = this;
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;

            _id = id;
        }

        private void SetupScene()
        {
            // setup lighting            
            this.AmbientLightColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
            this.DirectionalLightColor = SharpDX.Color.White;
            this.DirectionalLightDirection = new Vector3(-2, -2, -5);
            this.RenderTechnique = Techniques.RenderPhong;
            this.RedMaterial = PhongMaterials.Red;
            this.CyanMaterial = PhongMaterials.Turquoise;

            this.Model1Transform = new System.Windows.Media.Media3D.TranslateTransform3D(0, -0, 0);

            // camera setup
            this.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
            {
                Position = new Point3D(3, 3, 5),
                LookDirection = new Vector3D(-3, -3, -5),
                UpDirection = new Vector3D(0, 1, 0)
            };

            Mesh = InitMeshGeometry();
            MeshSelected = InitMeshGeometry();

            Lines = InitLineGeometry();
            LinesSelected = InitLineGeometry();

            Points = InitPointGeometry();
            PointsSelected = InitPointGeometry();

            DrawGrid();
        }

        #endregion

        #region event handlers

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Watch 3D view unloaded.");

            var vm = DataContext as IWatchViewModel;
            if (vm != null)
            {
                vm.VisualizationManager.RenderComplete -= VisualizationManagerRenderComplete;
                vm.VisualizationManager.ResultsReadyToVisualize -= VisualizationManager_ResultsReadyToVisualize;
            }
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            MouseLeftButtonDown += new MouseButtonEventHandler(view_MouseButtonIgnore);
            MouseLeftButtonUp += new MouseButtonEventHandler(view_MouseButtonIgnore);
            MouseRightButtonUp += new MouseButtonEventHandler(view_MouseRightButtonUp);
            PreviewMouseRightButtonDown += new MouseButtonEventHandler(view_PreviewMouseRightButtonDown);

            var vm = DataContext as IWatchViewModel;
            //check this for null so the designer can load the preview
            if (vm != null)
            {
                vm.VisualizationManager.RenderComplete += VisualizationManagerRenderComplete;
                vm.VisualizationManager.ResultsReadyToVisualize += VisualizationManager_ResultsReadyToVisualize;
            }
        }

        /// <summary>
        /// Handler for the visualization manager's ResultsReadyToVisualize event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VisualizationManager_ResultsReadyToVisualize(object sender, VisualizationEventArgs e)
        {
            if (CheckAccess())
                RenderDrawables(e);
            else
            {
                // Scheduler invokes ResultsReadyToVisualize on background thread.
                Dispatcher.BeginInvoke(new Action(() => RenderDrawables(e)));
            }
        }

        /// <summary>
        /// When visualization is complete, the view requests it's visuals. For Full
        /// screen watch, this will be all renderables. For a Watch 3D node, this will
        /// be the subset of the renderables associated with the node.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VisualizationManagerRenderComplete(object sender, RenderCompletionEventArgs e)
        {
            var executeCommand = new Action(delegate
            {
                var vm = (IWatchViewModel)DataContext;
                if (vm.GetBranchVisualizationCommand.CanExecute(e.TaskId))
                    vm.GetBranchVisualizationCommand.Execute(e.TaskId);
            });

            if (CheckAccess())
                executeCommand();
            else
                Dispatcher.BeginInvoke(executeCommand);
        }

        /// <summary>
        /// Callback for thumb control's DragStarted event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// Callbcak for thumb control's DragCompleted event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// Callback for thumb control's DragDelta event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            var yAdjust = ActualHeight + e.VerticalChange;
            var xAdjust = ActualWidth + e.HorizontalChange;

            //Debug.WriteLine("d_x:" + e.HorizontalChange + "," + "d_y:" + e.VerticalChange);
            //Debug.WriteLine("Size:" + _nodeUI.Width + "," + _nodeUI.Height);
            //Debug.WriteLine("ActualSize:" + _nodeUI.ActualWidth + "," + _nodeUI.ActualHeight);
            //Debug.WriteLine("Grid size:" + _nodeUI.ActualWidth + "," + _nodeUI.ActualHeight);

            if (xAdjust >= inputGrid.MinWidth)
            {
                Width = xAdjust;
            }

            if (yAdjust >= inputGrid.MinHeight)
            {
                Height = yAdjust;
            }
        }

        protected void OnZoomToFitClicked(object sender, RoutedEventArgs e)
        {
            watch_view.ZoomExtents();
        }

        void view_MouseButtonIgnore(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
        }

        void view_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _rightMousePoint = e.GetPosition(topControl);
        }

        void view_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            //if the mouse has moved, and this is a right click, we assume 
            // rotation. handle the event so we don't show the context menu
            // if the user wants the contextual menu they can click on the
            // node sidebar or top bar
            if (e.GetPosition(topControl) != _rightMousePoint)
            {
                e.Handled = true;
            }
        }

        private void Watch_view_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            //Point mousePos = e.GetPosition(watch_view);
            //PointHitTestParameters hitParams = new PointHitTestParameters(mousePos);
            //VisualTreeHelper.HitTest(watch_view, null, ResultCallback, hitParams);
            //e.Handled = true;
        }

        #endregion

        #region private methods

        /// <summary>
        /// Create the grid
        /// </summary>
        private void DrawGrid()
        {
            //var builder = new HelixToolkit.Wpf.SharpDX.LineBuilder();
            Grid = new LineGeometry3D();
            var positions = new Vector3Collection();
            var indices = new IntCollection();

            var size = 50;

            for (int x = -size; x <= size; x++)
            {
                //builder.AddLine(new Vector3(x, -.001f, -size), new Vector3(x, -.001f, size));
                positions.Add(new Vector3(x, -.001f, -size));
                indices.Add(positions.Count-1);
                positions.Add(new Vector3(x, -.001f, size));
                indices.Add(positions.Count-1);
                
            }

            for (int y = -size; y <= size; y++)
            {
                //builder.AddLine(new Vector3(-size, -.001f, y), new Vector3(size, -.001f, y));
                positions.Add(new Vector3(-size, -.001f, y)); 
                indices.Add(positions.Count-1);
                positions.Add(new Vector3(size, -.001f, y));
                indices.Add(positions.Count-1);
            }

            //Grid = builder.ToLineGeometry3D();
            Grid.Positions = positions;
            Grid.Indices = indices;
        }

        /// <summary>
        /// Use the render packages returned from the visualization manager to update the visuals.
        /// The visualization event arguments will contain a set of render packages and an id representing 
        /// the associated node. Visualizations for the background preview will return an empty id.
        /// </summary>
        /// <param name="e"></param>
        public void RenderDrawables(VisualizationEventArgs e)
        {
            //check the id, if the id is meant for another watch,
            //then ignore it
            if (e.Id != _id)
            {
                return;
            }

            Debug.WriteLine(string.Format("Rendering visuals for {0}", e.Id));

            var sw = new Stopwatch();
            sw.Start();

            Points = null;
            Lines = null;
            Mesh = null;
            XAxes = null;
            YAxes = null;
            ZAxes = null;
            PointsSelected = null;
            LinesSelected = null;
            MeshSelected = null;
            //Text = null;
            MeshCount = 0;

            //separate the selected packages
            var packages = e.Packages.Where(x => x.Selected == false)
                .Where(rp=>rp.TriangleVertices.Count % 9 == 0)
                .ToArray();
            var selPackages = e.Packages
                .Where(x => x.Selected)
                .Where(rp => rp.TriangleVertices.Count % 9 == 0)
                .ToArray();

            var points = InitPointGeometry();
            var pointsSelected = InitPointGeometry();

            var lines = InitLineGeometry();
            var linesSel = InitLineGeometry();

            var redLines = new LineGeometry3D();
            var greenLines = new LineGeometry3D();
            var blueLines = new LineGeometry3D();

            //pre-size the text collection
            var textCount = e.Packages.Count(x => x.DisplayLabels);
            var text = new List<BillboardTextItem>(textCount);

            var mesh = InitMeshGeometry();
            var meshSel = InitMeshGeometry();

            foreach (var package in packages)
            {
                ConvertPoints(package, points);
                ConvertLines(package, lines);
                ConvertMeshes(package, mesh);
            }

            foreach (var package in selPackages)
            {
                ConvertPoints(package, pointsSelected);

                ConvertLines(package, linesSel);

                ConvertMeshes(package, meshSel);
            }

            sw.Stop();
            Debug.WriteLine(string.Format("RENDER: {0} ellapsed for updating background preview.", sw.Elapsed));

            Dispatcher.Invoke(new Action<
                PointGeometry3D,
                PointGeometry3D,
                LineGeometry3D,
                LineGeometry3D,
                LineGeometry3D,
                LineGeometry3D,
                LineGeometry3D,
                HelixToolkit.Wpf.SharpDX.MeshGeometry3D,
                HelixToolkit.Wpf.SharpDX.MeshGeometry3D>(SendGraphicsToView), DispatcherPriority.Render,
                               new object[] {
                                   points, 
                                   pointsSelected, 
                                   lines, 
                                   linesSel, 
                                   redLines, 
                                   greenLines, 
                                   blueLines, 
                                   mesh, 
                                   meshSel});
        }

        private static LineGeometry3D InitLineGeometry()
        {
            var lines = new LineGeometry3D
            {
                Positions = new Vector3Collection(),
                Indices = new IntCollection(),
                Colors = new Color4Collection()
            };

            lines.Positions.Add(new Vector3());
            lines.Positions.Add(new Vector3());
            lines.Indices.Add(0);
            lines.Indices.Add(1);
            lines.Colors.Add(new Color4());
            lines.Colors.Add(new Color4());

            return lines;
        }

        private static PointGeometry3D InitPointGeometry()
        {
            var points = new PointGeometry3D()
            {
                Positions = new Vector3Collection(),
                Indices = new IntCollection(),
                Colors = new Color4Collection()
            };

            points.Positions.Add(new Vector3());
            points.Indices.Add(0);
            points.Colors.Add(new Color4());

            return points;
        }

        private static HelixToolkit.Wpf.SharpDX.MeshGeometry3D InitMeshGeometry()
        {
            var mesh = new HelixToolkit.Wpf.SharpDX.MeshGeometry3D()
            {
                Positions = new Vector3Collection(),
                Indices = new IntCollection(),
                Colors = new Color4Collection(),
                Normals = new Vector3Collection(),
            };

            mesh.Positions.Add(new Vector3());
            mesh.Positions.Add(new Vector3());
            mesh.Positions.Add(new Vector3());
            mesh.Normals.Add(new Vector3(0, 0, 1));
            mesh.Normals.Add(new Vector3(0, 0, 1));
            mesh.Normals.Add(new Vector3(0, 0, 1));
            mesh.Colors.Add(new Color4());
            mesh.Colors.Add(new Color4());
            mesh.Colors.Add(new Color4());
            mesh.Indices.Add(0);
            mesh.Indices.Add(1);
            mesh.Indices.Add(2);

            return mesh;
        }

        private void SendGraphicsToView(
            PointGeometry3D points,
            PointGeometry3D pointsSelected,
            LineGeometry3D lines,
            LineGeometry3D linesSelected,
            LineGeometry3D redLines,
            LineGeometry3D greenLines,
            LineGeometry3D blueLines, 
            HelixToolkit.Wpf.SharpDX.MeshGeometry3D mesh,
            HelixToolkit.Wpf.SharpDX.MeshGeometry3D meshSel)
        {
            Points = points;
            PointsSelected = pointsSelected;
            Lines = lines;
            LinesSelected = linesSelected;
            //XAxes = redLines;
            //YAxes = greenLines;
            //ZAxes = blueLines;
            Mesh = mesh;
            MeshSelected = meshSel;
            //Text = text;

            // Send property changed notifications for everything
            NotifyPropertyChanged(string.Empty);
        }

        private void ConvertPoints(RenderPackage p,
            ICollection<Point3D> pointColl)
        {
            for (int i = 0; i < p.PointVertices.Count; i += 3)
            {

                var pos = new Point3D(
                    p.PointVertices[i],
                    p.PointVertices[i + 1],
                    p.PointVertices[i + 2]);

                pointColl.Add(pos);

                if (p.DisplayLabels)
                {
                    //text.Add(new BillboardTextItem {Text = CleanTag(p.Tag), Position = pos});
                }
            }
        }

        private void ConvertLines(RenderPackage p,
            ICollection<Point3D> lineColl,
            ICollection<Point3D> redLines,
            ICollection<Point3D> greenLines,
            ICollection<Point3D> blueLines)
        {
            int idx = 0;
            int color_idx = 0;

            int outerCount = 0;
            foreach (var count in p.LineStripVertexCounts)
            {
                for (int i = 0; i < count; ++i)
                {
                    var point = new Point3D(p.LineStripVertices[idx], p.LineStripVertices[idx + 1],
                        p.LineStripVertices[idx + 2]);

                    if (i == 0 && outerCount == 0 && p.DisplayLabels)
                    {
                        //text.Add(new BillboardTextItem { Text = CleanTag(p.Tag), Position = point });
                    }

                    if (i != 0 && i != count - 1)
                    {
                        lineColl.Add(point);
                    }
                    
                    bool isAxis = false;
                    var startColor = Color.FromRgb(
                                            p.LineStripVertexColors[color_idx],
                                            p.LineStripVertexColors[color_idx + 1],
                                            p.LineStripVertexColors[color_idx + 2]);

                    if (startColor == Color.FromRgb(255, 0, 0))
                    {
                        redLines.Add(point);
                        isAxis = true;
                    }
                    else if (startColor == Color.FromRgb(0, 255, 0))
                    {
                        greenLines.Add(point);
                        isAxis = true;
                    }
                    else if (startColor == Color.FromRgb(0, 0, 255))
                    {
                        blueLines.Add(point);
                        isAxis = true;
                    }

                    if (!isAxis)
                    {
                        lineColl.Add(point);
                    } 

                    idx += 3;
                    color_idx += 4;
                }
                outerCount++;
            }
        }

        private void ConvertMeshes(RenderPackage p,
            ICollection<Vector3> points, ICollection<Vector3> norms,
            ICollection<int> tris)
        {
            for (int i = 0; i < p.TriangleVertices.Count; i+=3)
            {
                var new_point = new Vector3((float)p.TriangleVertices[i],
                                            (float)p.TriangleVertices[i + 1],
                                            (float)p.TriangleVertices[i + 2]);

                var normal = new Vector3((float)p.TriangleNormals[i],
                                            (float)p.TriangleNormals[i + 1],
                                            (float)p.TriangleNormals[i + 2]);
                    
                tris.Add(points.Count);
                points.Add(new_point);
                norms.Add(normal);
            }

            if (tris.Count > 0)
            {
                MeshCount++;
            }
        }

        private void ConvertPoints(RenderPackage p, PointGeometry3D points)
        {
            int color_idx = 0;

            for (int i = 0; i < p.PointVertices.Count; i += 3)
            {
                var x = (float)p.PointVertices[i];
                var y = (float)p.PointVertices[i + 1];
                var z = (float)p.PointVertices[i + 2];

                // DirectX convention - Y Up
                var pt = new Vector3(x, z, y);

                //if (i == 0 && outerCount == 0 && p.DisplayLabels)
                //{
                //    //text.Add(new BillboardTextItem { Text = CleanTag(p.Tag), Position = point });
                //}

                var ptColor = new SharpDX.Color4(
                                        (float)(p.PointVertexColors[color_idx] / 255),
                                        (float)(p.PointVertexColors[color_idx + 1] / 255),
                                        (float)(p.PointVertexColors[color_idx + 2] / 255), 1);
                
                points.Positions.Add(pt);
                points.Indices.Add(points.Positions.Count);
                points.Colors.Add(ptColor);
                color_idx += 4;
            }

        }

        private void ConvertLines(RenderPackage p, LineGeometry3D geom)
        {
            int color_idx = 0;
            var idx = geom.Indices.Count;

            for (int i = 0; i < p.LineStripVertices.Count; i += 3)
            {
                var x = (float)p.LineStripVertices[i];
                var y = (float)p.LineStripVertices[i + 1];
                var z = (float)p.LineStripVertices[i + 2];

                // DirectX convention - Y Up
                var ptA = new Vector3(x,z,y);

                //if (i == 0 && outerCount == 0 && p.DisplayLabels)
                //{
                //    //text.Add(new BillboardTextItem { Text = CleanTag(p.Tag), Position = point });
                //}

                var startColor = new SharpDX.Color4(
                                        (float)(p.LineStripVertexColors[color_idx] / 255),
                                        (float)(p.LineStripVertexColors[color_idx + 1] / 255),
                                        (float)(p.LineStripVertexColors[color_idx + 2] / 255), 1);

                geom.Indices.Add(idx);
                geom.Positions.Add(ptA);
                geom.Colors.Add(startColor);

                color_idx += 4;
                idx += 1;
            }

        }

        private void ConvertMeshes(RenderPackage p, HelixToolkit.Wpf.SharpDX.MeshGeometry3D mesh)
        {
            // DirectX has a different winding than we store in
            // render packages. Re-wind triangles here...
            int color_idx = 0;
            int pt_idx = mesh.Positions.Count;

            for (int i = 0; i < p.TriangleVertices.Count; i += 9)
            {
                var a = GetVertex(p.TriangleVertices, i);
                var b = GetVertex(p.TriangleVertices, i + 3);
                var c = GetVertex(p.TriangleVertices, i + 6);

                var an = GetVertex(p.TriangleNormals, i);
                var bn = GetVertex(p.TriangleNormals, i + 3);
                var cn = GetVertex(p.TriangleNormals, i + 6);

                var ca = GetColor(p, color_idx);
                var cb = GetColor(p, color_idx + 4);
                var cc = GetColor(p, color_idx + 8);

                mesh.Positions.Add(a);
                mesh.Positions.Add(c);
                mesh.Positions.Add(b);

                mesh.Indices.Add(pt_idx);
                mesh.Indices.Add(pt_idx + 1);
                mesh.Indices.Add(pt_idx + 2);

                mesh.Normals.Add(an);
                mesh.Normals.Add(cn);
                mesh.Normals.Add(bn);

                mesh.Colors.Add(ca);
                mesh.Colors.Add(cc);
                mesh.Colors.Add(cb);

                color_idx += 12;
                pt_idx += 3;
            }

            if (mesh.Indices.Count > 0)
            {
                MeshCount++;
            }

            // Backface rendering. Currently causes 
            // out of memory exceptions as we're doubling.
            // geometry inside the same mesh.

            //if (!p.TriangleVertices.Any()) return;

            // Create reversed backfaces
            //var copytri = new int[mesh.Indices.Count];
            //mesh.Indices.CopyTo(copytri);
            //Array.Reverse(copytri);

            //var copynorm = new Vector3[mesh.Normals.Count];
            //for (int i = 0; i < copynorm.Length; i++)
            //    copynorm[i] = -1 * mesh.Normals[i];

            //mesh.Positions.AddRange(mesh.Positions.ToArray());
            //mesh.Indices.AddRange(copytri);
            //mesh.Normals.AddRange(copynorm);
            //mesh.Colors.AddRange(mesh.Colors.ToArray());
        }

        private static Color4 GetColor(RenderPackage p, int color_idx)
        {
            var color = new Color4(
                (float)(p.TriangleVertexColors[color_idx]/255.0),
                (float)(p.TriangleVertexColors[color_idx + 1]/255.0),
                (float)(p.TriangleVertexColors[color_idx + 2]/255.0),
                1);

            if (color == new Color4(1, 1, 1, 1))
                color = new Color4(0.5f, 0.5f, 0.5f, 1);
            return color;
        }

        private static Vector3 GetVertex(List<double> p, int i)
        {
            var x = (float)p[i];
            var y = (float)p[i + 1];
            var z = (float)p[i + 2];

            // DirectX convention - Y Up
            var new_point = new Vector3(x, z, y);
            return new_point;
        }

        private string CleanTag(string tag)
        {
            var splits = tag.Split(':');
            if (splits.Count() <= 1) return tag;

            var sb = new StringBuilder();
            for (int i = 1; i < splits.Count(); i++)
            {
                sb.AppendFormat("[{0}]", splits[i]);
            }
            return sb.ToString();
        }

        #endregion
    }
}