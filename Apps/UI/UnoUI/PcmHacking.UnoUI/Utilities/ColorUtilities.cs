using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Utilities
{
    internal class ColorUtilities
    {
        private static ColorUtilities instance;

        public SolidColorBrush[] BackgroundBrushes { get; private set; }

        public SolidColorBrush DefaultBackgroundBrush { get; private set; }

        public SolidColorBrush AccentBackgroundBrush { get; private set; }

        public static ColorUtilities Instance
        {
            get
            {
                if (instance == null)
                {
                    throw new InvalidOperationException("ColorUtilities.Instance must not be accessed until after ColorUtilities.Initialize(bool darkMode) has been called.");
                }
                return instance;
            }
        }

        public static ColorUtilities Initialize(bool darkMode)
        {
            // It's tempting to lock here, but it won't matter if it gets called concurrently.
            if (instance == null)
            {
                instance = new ColorUtilities(darkMode);
            }

            return instance;
        }

        public ColorUtilities(bool darkMode)
        {
            byte dark = 40;
            byte light = 216;
            this.DefaultBackgroundBrush = darkMode ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Colors.White);
            this.AccentBackgroundBrush = darkMode ? new SolidColorBrush(ColorHelper.FromArgb(255, dark, dark, dark)) : new SolidColorBrush(ColorHelper.FromArgb(255, light, light, light));
            this.BackgroundBrushes = new SolidColorBrush[] { this.DefaultBackgroundBrush, this.AccentBackgroundBrush };
        }
    }
}
