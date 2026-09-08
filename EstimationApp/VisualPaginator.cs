using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EstimationApp
{
    public class VisualPaginator : DocumentPaginator
    {
        private UIElement _visual;
        private Size _pageSize;
        private Size _margin;
        private int _pageCount;
        private double _scale;
        private double _visualPageHeight;

        public VisualPaginator(UIElement visual, Size pageSize, Size margin)
        {
            _visual = visual;
            _pageSize = pageSize;
            _margin = margin;

            // 1. Force the visual to render at its full natural height
            _visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _visual.Arrange(new Rect(new Point(0, 0), _visual.DesiredSize));
            _visual.UpdateLayout();

            // 2. Calculate the available printable area on the A4 page
            double pageContentWidth = _pageSize.Width - (_margin.Width * 2);
            double pageContentHeight = _pageSize.Height - (_margin.Height * 2);

            // 3. Calculate how much we need to shrink the visual to fit the page width safely
            _scale = Math.Min(1.0, pageContentWidth / _visual.DesiredSize.Width);

            // 4. Calculate how much of the original visual's height fits onto one printed page
            _visualPageHeight = pageContentHeight / _scale;

            // 5. Determine total pages needed
            _pageCount = (int)Math.Ceiling(_visual.DesiredSize.Height / _visualPageHeight);
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            double pageContentWidth = _pageSize.Width - (_margin.Width * 2);
            double pageContentHeight = _pageSize.Height - (_margin.Height * 2);

            DrawingVisual drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                // Create a brush from your UI element
                VisualBrush visualBrush = new VisualBrush(_visual);
                visualBrush.Stretch = Stretch.Uniform;
                visualBrush.AlignmentX = AlignmentX.Left;
                visualBrush.AlignmentY = AlignmentY.Top;

                // Map the exact vertical slice of the original visual for this specific page
                visualBrush.ViewboxUnits = BrushMappingMode.Absolute;
                visualBrush.Viewbox = new Rect(0, pageNumber * _visualPageHeight, _visual.DesiredSize.Width, _visualPageHeight);

                // Draw the scaled slice onto the page, respecting the margins
                dc.DrawRectangle(visualBrush, null, new Rect(_margin.Width, _margin.Height, pageContentWidth, pageContentHeight));
            }

            return new DocumentPage(drawingVisual, _pageSize, new Rect(_pageSize), new Rect(_margin.Width, _margin.Height, pageContentWidth, pageContentHeight));
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _pageCount;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource Source => null;
    }
}