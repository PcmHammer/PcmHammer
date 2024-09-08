using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class MainForm
    {
        private static Brush textBrush = Brushes.White;
        private static Font bigFont = new Font("Arial", 32);
        private static Font littleFont = new Font("Arial", 16);
        private const int margin = 5;
        
        public void ClearZoomPanel()
        {
            var canvas = this.splitContainer2.Panel2;
            using (var graphics = canvas.CreateGraphics())
            {
                graphics.FillRectangle(new SolidBrush(Color.Black), canvas.DisplayRectangle);
            }
        }

        public void DrawZoomedParameters(List<ZoomedParameter> list)
        {
            
            var canvas = this.splitContainer2.Panel2;
            using (var graphics = canvas.CreateGraphics())
            using (var buffer = BufferedGraphicsManager.Current.Allocate(graphics, canvas.DisplayRectangle))
            {
                graphics.FillRectangle(new SolidBrush(Color.Black), canvas.DisplayRectangle);
                var rowHeight = canvas.DisplayRectangle.Height / list.Count;

                for(int row = 0; row < list.Count; row++)
                {
                    var centerX = canvas.DisplayRectangle.Width / 2;
                    var centerY = ((rowHeight) / 2) + row * rowHeight;

                    StringFormat format = new StringFormat();

                    string valueString = list[row].Value;
                    SizeF valueSize = buffer.Graphics.MeasureString(valueString, bigFont);
                    float valueX = centerX - valueSize.Width / 2;
                    float valueY = centerY - valueSize.Height / 2;
                    buffer.Graphics.DrawString(valueString, bigFont, textBrush, valueX, valueY, format);

                    string nameString = list[row].Name;
                    SizeF nameSize = buffer.Graphics.MeasureString(nameString, littleFont);
                    float nameX = centerX - nameSize.Width / 2;
                    float nameY = centerY - ((valueSize.Height / 2) + nameSize.Width / 2 + margin);
                    buffer.Graphics.DrawString(nameString, littleFont, textBrush, nameX, nameY, format);

                    string unitsString = list[row].Units;
                    SizeF unitsSize = buffer.Graphics.MeasureString(unitsString, littleFont);
                    float unitsX = centerX - unitsSize.Width / 2;
                    float unitsY = centerY + (valueSize.Height / 2) + margin;
                    buffer.Graphics.DrawString(unitsString, littleFont, textBrush, unitsX, unitsY, format);
                }

                buffer.Render();
            }

        }
    }
}
