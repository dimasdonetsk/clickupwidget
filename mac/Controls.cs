using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClickUpWidget.Mac
{
    /// <summary>Рядок кольору (#RRGGBB з Palette) → пензель. Пензлі кешуються.</summary>
    public sealed class HexBrush : IValueConverter
    {
        private static readonly Dictionary<string, IBrush> Cache = new();

        public static IBrush Get(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
            if (Cache.TryGetValue(hex, out IBrush? cached)) return cached;
            IBrush brush = Color.TryParse(hex, out Color c) ? new ImmutableSolidColorBrush(c) : Brushes.Gray;
            if (Cache.Count < 512) Cache[hex] = brush;
            return brush;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Get(value as string);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Маленька векторна іконка (коментар, статус, таймер…). Геометрія — у квадраті 16×16,
    /// колір — успадкований Foreground (як у тексту кнопки). Замість шрифту Segoe Fluent Icons, якого на Mac немає.
    /// </summary>
    public sealed class Glyph : Control
    {
        public static readonly StyledProperty<Geometry?> DataProperty =
            AvaloniaProperty.Register<Glyph, Geometry?>(nameof(Data));

        public static readonly StyledProperty<bool> FilledProperty =
            AvaloniaProperty.Register<Glyph, bool>(nameof(Filled));

        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<Glyph>();

        static Glyph()
        {
            AffectsRender<Glyph>(DataProperty, FilledProperty, ForegroundProperty);
        }

        public Glyph()
        {
            Width = 11;
            Height = 11;
            IsHitTestVisible = false;
        }

        public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
        public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
        public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        public override void Render(DrawingContext context)
        {
            if (Data == null || Foreground == null) return;
            double scale = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                if (Filled) context.DrawGeometry(Foreground, null, Data);
                else
                {
                    // Лінія ~1.4 px на екрані незалежно від розміру іконки.
                    var pen = new Pen(Foreground, 1.4 / Math.Max(scale, 0.1),
                        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    context.DrawGeometry(null, pen, Data);
                }
            }
        }
    }
}
