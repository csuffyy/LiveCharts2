﻿// The MIT License(MIT)
//
// Copyright(c) 2021 Alberto Rodriguez Orozco & LiveCharts Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Common;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Data;
using LiveChartsCore.Kernel.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace LiveChartsCore
{
    /// <summary>
    /// Defines a column series.
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TVisual"></typeparam>
    /// <typeparam name="TLabel"></typeparam>
    /// <typeparam name="TDrawingContext"></typeparam>
    public abstract class HeatSeries<TModel, TVisual, TLabel, TDrawingContext> : CartesianSeries<TModel, TVisual, TLabel, TDrawingContext>, IHeatSeries<TDrawingContext>
        where TVisual : class, ISolidColorChartPoint<TDrawingContext>, new()
        where TDrawingContext : DrawingContext
        where TLabel : class, ILabelGeometry<TDrawingContext>, new()
    {
        private IPaintTask<TDrawingContext>? _paintTaks;
        private Bounds _weightBounds = new();
        private int _heatKnownLength = 0;
        private List<Tuple<double, Color>> _heatStops = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="HeatSeries{TModel, TVisual, TLabel, TDrawingContext}"/> class.
        /// </summary>
        public HeatSeries()
            : base(
                 SeriesProperties.Heat | SeriesProperties.PrimaryAxisVerticalOrientation |
                 SeriesProperties.Solid | SeriesProperties.PrefersXYStrategyTooltips)
        {
            HoverState = LiveCharts.HeatSeriesHoverState;
            DataPadding = new PointF(0, 0);
            TooltipLabelFormatter = (point) => $"{Name}: {point.TertiaryValue:N}";
        }

        /// <inheritdoc cref="IHeatSeries{TDrawingContext}.HeatMap"/>
        public Color[] HeatMap { get; set; } = new[]
        {
            Color.FromArgb(255, 87, 103, 222), // cold (min value)
            Color.FromArgb(255, 95, 207, 249) // hot (max value)
        };

        /// <inheritdoc cref="IHeatSeries{TDrawingContext}.ColorStops"/>
        public double[]? ColorStops { get; set; }

        /// <inheritdoc cref="IHeatSeries{TDrawingContext}.PointPadding"/>
        public Padding PointPadding { get; set; } = new Padding(4);

        /// <inheritdoc cref="ChartElement{TDrawingContext}.Measure(Chart{TDrawingContext})"/>
        public override void Measure(Chart<TDrawingContext> chart)
        {
            if (_paintTaks == null) _paintTaks = GetSolidColorPaintTask();

            var cartesianChart = (CartesianChart<TDrawingContext>)chart;
            var primaryAxis = cartesianChart.YAxes[ScalesYAt];
            var secondaryAxis = cartesianChart.XAxes[ScalesXAt];

            var drawLocation = cartesianChart.DrawMarginLocation;
            var drawMarginSize = cartesianChart.DrawMarginSize;
            var secondaryScale = new Scaler(drawLocation, drawMarginSize, secondaryAxis);
            var primaryScale = new Scaler(drawLocation, drawMarginSize, primaryAxis);
            var previousPrimaryScale =
                primaryAxis.PreviousDataBounds == null ? null : new Scaler(drawLocation, drawMarginSize, primaryAxis, true);
            var previousSecondaryScale =
                secondaryAxis.PreviousDataBounds == null ? null : new Scaler(drawLocation, drawMarginSize, secondaryAxis, true);

            var uws = secondaryScale.ToPixels((float)secondaryAxis.UnitWidth) - secondaryScale.ToPixels(0f);
            var uwp = primaryScale.ToPixels(0f) - primaryScale.ToPixels((float)primaryAxis.UnitWidth);

            var actualZIndex = ZIndex == 0 ? ((ISeries)this).SeriesId : ZIndex;
            if (_paintTaks != null)
            {
                _paintTaks.ZIndex = actualZIndex + 0.2;
                _paintTaks.SetClipRectangle(cartesianChart.Canvas, new RectangleF(drawLocation, drawMarginSize));
                cartesianChart.Canvas.AddDrawableTask(_paintTaks);
            }
            if (DataLabelsPaint != null)
            {
                DataLabelsPaint.ZIndex = actualZIndex + 0.3;
                DataLabelsPaint.SetClipRectangle(cartesianChart.Canvas, new RectangleF(drawLocation, drawMarginSize));
                cartesianChart.Canvas.AddDrawableTask(DataLabelsPaint);
            }

            var dls = (float)DataLabelsSize;
            var toDeletePoints = new HashSet<ChartPoint>(everFetched);

            var p = PointPadding;

            BuildColorStops();

            foreach (var point in Fetch(cartesianChart))
            {
                var visual = point.Context.Visual as TVisual;
                var primary = primaryScale.ToPixels(point.PrimaryValue);
                var secondary = secondaryScale.ToPixels(point.SecondaryValue);
                var tertiary = point.TertiaryValue;

                var baseColor = InterpolateColor(tertiary);

                if (point.IsNull)
                {
                    if (visual != null)
                    {
                        visual.X = secondary - uws * 0.5f;
                        visual.Y = primary - uwp * 0.5f;
                        visual.Width = uws;
                        visual.Height = uwp;
                        visual.RemoveOnCompleted = true;
                        visual.Color = Color.FromArgb(0, visual.Color);
                        point.Context.Visual = null;
                    }
                    continue;
                }

                if (visual == null)
                {
                    var xi = secondary - uws * 0.5f;
                    var yi = primary - uwp * 0.5f;

                    if (previousSecondaryScale != null && previousPrimaryScale != null)
                    {
                        var previousP = previousPrimaryScale.ToPixels(pivot);
                        var previousPrimary = previousPrimaryScale.ToPixels(point.PrimaryValue);
                        var bp = Math.Abs(previousPrimary - previousP);
                        var cyp = point.PrimaryValue > pivot ? previousPrimary : previousPrimary - bp;

                        xi = previousSecondaryScale.ToPixels(point.SecondaryValue) - uws * 0.5f;
                        yi = previousPrimaryScale.ToPixels(point.PrimaryValue) - uwp * 0.5f;
                    }

                    var r = new TVisual
                    {
                        X = xi + p.Left,
                        Y = yi + p.Top,
                        Width = uws - p.Left - p.Right,
                        Height = uwp - p.Top - p.Bottom,
                        Color = Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B)
                    };

                    visual = r;
                    point.Context.Visual = visual;
                    OnPointCreated(point);
                    r.CompleteAllTransitions();

                    _ = everFetched.Add(point);
                }

                if (_paintTaks != null) _paintTaks.AddGeometryToPaintTask(cartesianChart.Canvas, visual);

                visual.X = secondary - uws * 0.5f + p.Left;
                visual.Y = primary - uwp * 0.5f + p.Top;
                visual.Width = uws - p.Left - p.Right;
                visual.Height = uwp - p.Top - p.Bottom;
                visual.Color = Color.FromArgb(baseColor.A, baseColor.R, baseColor.G, baseColor.B);
                visual.RemoveOnCompleted = false;

                var ha = new RectangleHoverArea().SetDimensions(secondary - uws * 0.5f, primary - uwp * 0.5f, uws, uwp);
                point.Context.HoverArea = ha;

                OnPointMeasured(point);
                _ = toDeletePoints.Remove(point);

                if (DataLabelsPaint != null)
                {
                    var label = (TLabel?)point.Context.Label;

                    if (label == null)
                    {
                        var l = new TLabel { X = secondary - uws * 0.5f, Y = primary - uws * 0.5f };

                        _ = l.TransitionateProperties(nameof(l.X), nameof(l.Y))
                            .WithAnimation(animation =>
                                animation
                                    .WithDuration(AnimationsSpeed ?? cartesianChart.AnimationsSpeed)
                                    .WithEasingFunction(EasingFunction ?? cartesianChart.EasingFunction));

                        l.CompleteAllTransitions();
                        label = l;
                        point.Context.Label = l;
                    }

                    DataLabelsPaint.AddGeometryToPaintTask(cartesianChart.Canvas, label);

                    label.Text = DataLabelsFormatter(new TypedChartPoint<TModel, TVisual, TLabel, TDrawingContext>(point));
                    label.TextSize = dls;
                    label.Padding = DataLabelsPadding;
                    var labelPosition = GetLabelPosition(
                         secondary, primary, uws, uws, label.Measure(DataLabelsPaint), DataLabelsPosition, SeriesProperties, point.PrimaryValue > Pivot);
                    label.X = labelPosition.X;
                    label.Y = labelPosition.Y;
                }
            }

            foreach (var point in toDeletePoints)
            {
                if (point.Context.Chart != cartesianChart.View) continue;
                SoftDeletePoint(point, primaryScale, secondaryScale);
                _ = everFetched.Remove(point);
            }
        }

        /// <inheritdoc cref="ICartesianSeries{TDrawingContext}.GetBounds(CartesianChart{TDrawingContext}, IAxis{TDrawingContext}, IAxis{TDrawingContext})"/>
        public override SeriesBounds GetBounds(
            CartesianChart<TDrawingContext> chart, IAxis<TDrawingContext> secondaryAxis, IAxis<TDrawingContext> primaryAxis)
        {
            var baseSeriesBounds = base.GetBounds(chart, secondaryAxis, primaryAxis);
            if (baseSeriesBounds.IsPrevious) return baseSeriesBounds;
            var baseBounds = baseSeriesBounds.Bounds;

            _weightBounds = baseBounds.TertiaryBounds;

            var tickPrimary = primaryAxis.GetTick(chart.ControlSize, baseBounds.VisiblePrimaryBounds);
            var tickSecondary = secondaryAxis.GetTick(chart.ControlSize, baseBounds.VisibleSecondaryBounds);

            var ts = tickSecondary.Value * DataPadding.X;
            var tp = tickPrimary.Value * DataPadding.Y;

            if (baseBounds.VisibleSecondaryBounds.Delta == 0)
            {
                var ms = baseBounds.VisibleSecondaryBounds.Min == 0 ? 1 : baseBounds.VisibleSecondaryBounds.Min;
                ts = 0.1 * ms * DataPadding.X;
            }

            if (baseBounds.VisiblePrimaryBounds.Delta == 0)
            {
                var mp = baseBounds.VisiblePrimaryBounds.Min == 0 ? 1 : baseBounds.VisiblePrimaryBounds.Min;
                tp = 0.1 * mp * DataPadding.Y;
            }

            return
                new SeriesBounds(
                    new DimensionalBounds
                    {
                        SecondaryBounds = new Bounds
                        {
                            Max = baseBounds.SecondaryBounds.Max + 0.5 * secondaryAxis.UnitWidth + ts,
                            Min = baseBounds.SecondaryBounds.Min - 0.5 * secondaryAxis.UnitWidth - ts
                        },
                        PrimaryBounds = new Bounds
                        {
                            Max = baseBounds.PrimaryBounds.Max + 0.5 * primaryAxis.UnitWidth + tp,
                            Min = baseBounds.PrimaryBounds.Min - 0.5 * primaryAxis.UnitWidth - tp
                        },
                        VisibleSecondaryBounds = new Bounds
                        {
                            Max = baseBounds.VisibleSecondaryBounds.Max + 0.5 * secondaryAxis.UnitWidth + ts,
                            Min = baseBounds.VisibleSecondaryBounds.Min - 0.5 * secondaryAxis.UnitWidth - ts
                        },
                        VisiblePrimaryBounds = new Bounds
                        {
                            Max = baseBounds.VisiblePrimaryBounds.Max + 0.5 * primaryAxis.UnitWidth + tp,
                            Min = baseBounds.VisiblePrimaryBounds.Min - 0.5 * primaryAxis.UnitWidth - tp
                        },
                        MinDeltaPrimary = baseBounds.MinDeltaPrimary,
                        MinDeltaSecondary = baseBounds.MinDeltaSecondary
                    },
                    false);
        }

        /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.SetDefaultPointTransitions(ChartPoint)"/>
        protected override void SetDefaultPointTransitions(ChartPoint chartPoint)
        {
            var chart = chartPoint.Context.Chart;

            if (chartPoint.Context.Visual is not TVisual visual) throw new Exception("Unable to initialize the point instance.");

            _ = visual
                .TransitionateProperties(
                    nameof(visual.X),
                    nameof(visual.Width),
                    nameof(visual.Y),
                    nameof(visual.Height),
                    nameof(visual.Color))
                .WithAnimation(animation =>
                    animation
                        .WithDuration(AnimationsSpeed ?? chart.AnimationsSpeed)
                        .WithEasingFunction(EasingFunction ?? chart.EasingFunction));
        }

        /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.SoftDeletePoint(ChartPoint, Scaler, Scaler)"/>
        protected override void SoftDeletePoint(ChartPoint point, Scaler primaryScale, Scaler secondaryScale)
        {
            var visual = (TVisual?)point.Context.Visual;
            if (visual == null) return;

            var chartView = (ICartesianChartView<TDrawingContext>)point.Context.Chart;
            if (chartView.Core.IsZoomingOrPanning)
            {
                visual.CompleteAllTransitions();
                visual.RemoveOnCompleted = true;
                return;
            }

            visual.Color = Color.FromArgb(255, visual.Color);
            visual.RemoveOnCompleted = true;

            if (dataProvider == null) throw new Exception("Data provider not found");
            dataProvider.DisposePoint(point);

            var label = (TLabel?)point.Context.Label;
            if (label == null) return;

            label.TextSize = 1;
            label.RemoveOnCompleted = true;
        }

        /// <summary>
        /// returns a new solid color paint task.
        /// </summary>
        /// <returns></returns>
        protected abstract IPaintTask<TDrawingContext> GetSolidColorPaintTask();

        /// <summary>
        /// Called when the paint context changes.
        /// </summary>
        protected override void OnSeriesMiniatureChanged()
        {
            var context = new CanvasSchedule<TDrawingContext>();
            var w = LegendShapeSize;
            var sh = 0f;
            //if (Stroke != null)
            //{
            //    var strokeClone = Stroke.CloneTask();
            //    var visual = new TVisual
            //    {
            //        X = strokeClone.StrokeThickness,
            //        Y = strokeClone.StrokeThickness,
            //        Height = (float)LegendShapeSize,
            //        Width = (float)LegendShapeSize
            //    };
            //    sh = strokeClone.StrokeThickness;
            //    strokeClone.ZIndex = 1;
            //    w += 2 * strokeClone.StrokeThickness;
            //    context.PaintSchedules.Add(new PaintSchedule<TDrawingContext>(strokeClone, visual));
            //}

            //if (Fill != null)
            //{
            //    var fillClone = Fill.CloneTask();
            //    var visual = new TVisual { X = sh, Y = sh, Height = (float)LegendShapeSize, Width = (float)LegendShapeSize };
            //    context.PaintSchedules.Add(new PaintSchedule<TDrawingContext>(fillClone, visual));
            //}

            context.Width = w;
            context.Height = w;

            canvaSchedule = context;

            OnPropertyChanged(nameof(CanvasSchedule));
        }

        /// <inheritdoc cref="ChartSeries{TModel, TVisual, TLabel, TDrawingContext}.MiniatureEquals(IChartSeries{TDrawingContext})"/>
        public override bool MiniatureEquals(IChartSeries<TDrawingContext> instance)
        {
            return instance is HeatSeries<TModel, TVisual, TLabel, TDrawingContext> hSeries
                && Name == instance.Name && HeatMap == hSeries.HeatMap;
        }

        /// <inheritdoc cref="ChartElement{TDrawingContext}.GetPaintTasks"/>
        protected override IPaintTask<TDrawingContext>?[] GetPaintTasks()
        {
            return new[] { _paintTaks };
        }

        /// <summary>
        /// Deletes the series from the user interface.
        /// </summary>
        /// <param name="chart"></param>
        /// <inheritdoc cref="M:LiveChartsCore.ISeries.Delete(LiveChartsCore.Kernel.IChartView)" />
        public override void SoftDelete(IChartView chart)
        {
            var core = ((ICartesianChartView<TDrawingContext>)chart).Core;

            var secondaryAxis = core.XAxes[ScalesXAt];
            var primaryAxis = core.YAxes[ScalesYAt];

            var secondaryScale = new Scaler(core.DrawMarginLocation, core.DrawMarginSize, secondaryAxis);
            var primaryScale = new Scaler(core.DrawMarginLocation, core.DrawMarginSize, primaryAxis);

            var deleted = new List<ChartPoint>();
            foreach (var point in everFetched)
            {
                if (point.Context.Chart != chart) continue;

                SoftDeletePoint(point, primaryScale, secondaryScale);
                deleted.Add(point);
            }

            if (_paintTaks != null) core.Canvas.RemovePaintTask(_paintTaks);
            if (DataLabelsPaint != null) core.Canvas.RemovePaintTask(DataLabelsPaint);

            foreach (var item in deleted) _ = everFetched.Remove(item);
        }

        private void BuildColorStops()
        {
            if (_heatKnownLength == HeatMap.Length) return;

            if (HeatMap.Length < 2) throw new Exception("At least 2 colors are required in a heat map.");

            if (ColorStops == null)
            {
                var s = 1 / (double)(HeatMap.Length - 1);
                ColorStops = new double[HeatMap.Length];
                var x = 0d;
                for (var i = 0; i < HeatMap.Length; i++)
                {
                    ColorStops[i] = x;
                    x += s;
                }
            }

            if (ColorStops.Length != HeatMap.Length)
                throw new Exception($"{nameof(ColorStops)} and {nameof(HeatMap)} must have the same length.");

            _heatStops = new List<Tuple<double, Color>>();
            for (var i = 0; i < ColorStops.Length; i++)
            {
                _heatStops.Add(new Tuple<double, Color>(ColorStops[i], HeatMap[i]));
            }

            _heatKnownLength = HeatMap.Length;
        }

        private Color InterpolateColor(float weight)
        {
            var p = (weight - _weightBounds.Min) / (_weightBounds.Max - _weightBounds.Min);
            if (p < 0) p = 0;
            if (p > 1) p = 1;

            var previous = _heatStops[0];

            for (var i = 1; i < _heatStops.Count; i++)
            {
                var next = _heatStops[i];

                if (next.Item1 < p)
                {
                    previous = _heatStops[i];
                    continue;
                }

                var px = (p - previous.Item1) / (next.Item1 - previous.Item1);

                return Color.FromArgb(
                    (int)(previous.Item2.A + px * (next.Item2.A - previous.Item2.A)),
                    (int)(previous.Item2.R + px * (next.Item2.R - previous.Item2.R)),
                    (int)(previous.Item2.G + px * (next.Item2.G - previous.Item2.G)),
                    (int)(previous.Item2.B + px * (next.Item2.B - previous.Item2.B)));
            }

            return HeatMap[HeatMap.Length - 1];
        }
    }
}
