using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;
using KickForgeDesktop.Models;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// sohbet satırını kurar: saat · rozetler · isim · mesaj. rozetler
    /// InlineUIContainer olarak akıyor, böylece isimle aynı satırda duruyor
    /// ve uzun mesaj kendi kendine alt satıra sarıyor. satır ilk kez
    /// göründüğünde süzülerek gelir, rozetler tek tek yerine oturur.
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

            if (message.IsSystem)
            {
                block.Inlines.Add(new Run(message.Content)
                {
                    Foreground = ThemeManager.Get("TextDim"),
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            block.Inlines.Add(new Run(message.Clock)
            {
                Foreground = ThemeManager.Get("TextDim"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 9.5
            });
            block.Inlines.Add(new Run(" ") { FontSize = 6 });

            var pills = new List<FrameworkElement>();
            foreach (var badge in message.Badges)
            {
                var pill = Pill(badge);
                pills.Add(pill);
                block.Inlines.Add(new InlineUIContainer(pill) { BaselineAlignment = BaselineAlignment.Center });
                block.Inlines.Add(new Run(" ") { FontSize = 6 });
            }

            if (message.IsBot)
            {
                var bot = Pill(new ChatBadge
                {
                    Text = "",
                    Kind = "bot",
                    Icon = Mdi.Bot,
                    Hint = "bot",
                    Color = Color.FromRgb(0x9F, 0xA8, 0xC4)
                });
                pills.Add(bot);
                block.Inlines.Add(new InlineUIContainer(bot) { BaselineAlignment = BaselineAlignment.Center });
                block.Inlines.Add(new Run(" ") { FontSize = 6 });
            }

            block.Inlines.Add(new Run(message.User + "  ")
            {
                Foreground = message.UserBrush,
                FontWeight = FontWeights.SemiBold,
                Background = message.IsMine ? ThemeManager.Get("Surface3") : null
            });

            AppendContent(block, message.Content);

            // her satır yalnızca bir kez canlanır: geri kaydırınca tekrar oynamaz
            if (message.Animated) return;
            message.Animated = true;

            Motion.RiseIn(block, 6, Motion.Normal);
            for (var i = 0; i < pills.Count; i++)
            {
                Motion.PopIn(pills[i], 0.45, 300, 70 + i * 45);
            }
        }

        // kick ifadeleri metinde [emote:id:ad] olarak geliyor: yerine görselini koyuyoruz
        private static readonly Regex EmoteToken =
            new Regex(@"\[emote:(\d+):([A-Za-z0-9_]+)\]", RegexOptions.Compiled);

        private static void AppendContent(TextBlock block, string content)
        {
            var color = ThemeManager.Get("Text");
            var at = 0;

            foreach (Match match in EmoteToken.Matches(content))
            {
                if (match.Index > at)
                {
                    block.Inlines.Add(new Run(content.Substring(at, match.Index - at)) { Foreground = color });
                }

                block.Inlines.Add(new InlineUIContainer(
                    new EmoteView(match.Groups[1].Value, match.Groups[2].Value))
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                block.Inlines.Add(new Run(" ") { FontSize = 5 });

                at = match.Index + match.Length;
            }

            if (at < content.Length)
            {
                block.Inlines.Add(new Run(content.Substring(at)) { Foreground = color });
            }
        }

        /// <summary>ikon + yazı taşıyan küçük hap. ikon material design, font gerekmiyor.</summary>
        private static Border Pill(ChatBadge badge)
        {
            var color = badge.Color;
            var hasIcon = !string.IsNullOrEmpty(badge.Icon) && Mdi.Has(badge.Icon);

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (hasIcon)
            {
                content.Children.Add(Mdi.Shape(badge.Icon, badge.Kind == "level" ? 9.5 : 10, color));
            }

            if (badge.Text.Length > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = badge.Text,
                    Foreground = new SolidColorBrush(color),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(hasIcon ? 3 : 0, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var pill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(hasIcon ? 3 : 4, 0, 4, 1),
                Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Child = content,
                ToolTip = string.IsNullOrEmpty(badge.Hint) ? null : badge.Hint
            };

            ToolTipService.SetInitialShowDelay(pill, 400);
            ToolTipService.SetShowDuration(pill, 4000);

            pill.MouseEnter += (_, _) => Motion.Hover(pill, true);
            pill.MouseLeave += (_, _) => Motion.Hover(pill, false);
            return pill;
        }
    }
}
