using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KickForgeDesktop.Models;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// sohbet satırını kurar: saat · rozetler · isim · mesaj. rozetler
    /// InlineUIContainer olarak akıyor, böylece isimle aynı satırda duruyor
    /// ve uzun mesaj kendi kendine alt satıra sarıyor.
    /// </summary>
    public static class ChatLine
    {
        public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
            "Message", typeof(ChatMessage), typeof(ChatLine),
            new PropertyMetadata(null, OnMessageChanged));

        public static void SetMessage(DependencyObject element, ChatMessage? value) =>
            element.SetValue(MessageProperty, value);

        public static ChatMessage? GetMessage(DependencyObject element) =>
            (ChatMessage?)element.GetValue(MessageProperty);

        private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock block) return;

            block.Inlines.Clear();
            if (e.NewValue is not ChatMessage message) return;

            block.Inlines.Add(new Run(message.Clock)
            {
                Foreground = ThemeManager.Get("TextDim"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 9.5
            });
            block.Inlines.Add(new Run(" ") { FontSize = 6 });

            foreach (var badge in message.Badges)
            {
                block.Inlines.Add(new InlineUIContainer(Badge(badge)) { BaselineAlignment = BaselineAlignment.Center });
                block.Inlines.Add(new Run(" ") { FontSize = 6 });
            }

            block.Inlines.Add(new Run(message.User + "  ")
            {
                Foreground = message.UserBrush,
                FontWeight = FontWeights.SemiBold
            });

            if (message.IsBot)
            {
                block.Inlines.Add(new Run("bot") { FontSize = 8, Foreground = ThemeManager.Get("TextDim") });
                block.Inlines.Add(new Run("  ") { FontSize = 6 });
            }

            block.Inlines.Add(new Run(message.Content) { Foreground = ThemeManager.Get("Text") });
        }

        private static Border Badge(ChatBadge badge)
        {
            var color = badge.Color;

            return new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 1),
                Margin = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0x2C, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = badge.Text,
                    Foreground = new SolidColorBrush(color),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    BaselineOffset = 0
                }
            };
        }
    }
}
