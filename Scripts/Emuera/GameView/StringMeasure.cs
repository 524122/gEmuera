using System;
using System.Collections.Generic;
using System.Text;
//using System.Drawing;
using MinorShift.Emuera.Sub;
using MinorShift._Library;
//using System.Windows.Forms;
using uEmuera.Drawing;

namespace MinorShift.Emuera.GameView
{

	/// <summary>
	/// テキスト長計測装置
	/// 1819 必要になるたびにCreateGraphicsする方式をやめてあらかじめGraphicsを用意しておくことにする
	/// </summary>
	internal sealed class StringMeasure : IDisposable
	{
		public StringMeasure()
		{
			textDrawingMode = Config.TextDrawingMode;
			//layoutSize = new Size(Config.WindowX * 2, Config.LineHeight);
			//layoutRect = new RectangleF(0, 0, Config.WindowX * 2, Config.LineHeight);
			//fontDisplaySize = Config.Font.Size / 2 * 1.04f;//実際には指定したフォントより若干幅をとる？
			////bmp = new Bitmap(Config.WindowX, Config.LineHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			//bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			//graph = Graphics.FromImage(bmp);
			//if (textDrawingMode == TextDrawingMode.WINAPI)
			//	GDI.GdiMesureTextStart(graph);
		}

		readonly TextDrawingMode textDrawingMode;
		//readonly StringFormat sf = new StringFormat(StringFormatFlags.MeasureTrailingSpaces);
		//readonly CharacterRange[] ranges = new CharacterRange[] { new CharacterRange(0, 1) };
		//readonly Size layoutSize;
		//readonly RectangleF layoutRect;
		//readonly float fontDisplaySize;

		//readonly Graphics graph = null;
		//readonly Bitmap bmp = null;

		public int GetDisplayLength(string s, Font font)
		{
			if (string.IsNullOrEmpty(s))
				return 0;
			if (textDrawingMode == TextDrawingMode.GRAPHICS && s.Contains("\t"))
				s = s.Replace("\t", "        ");
			// Godot 的真实字形测量会随平台字体 fallback 改变；控制台布局必须与
			// STRLEN/半角全角单元格一致，否则中文地图和箱线字符在 Android 上列错位。
			return uEmuera.Utils.GetDisplayLength(s, font);
		}


		//bool disposed = false;
		public void Dispose()
		{
			//if (disposed)
			//	return;
			//disposed = true;
			//if (textDrawingMode == TextDrawingMode.WINAPI)
			//	GDI.GdiMesureTextEnd(graph);
			//graph.Dispose();
			//bmp.Dispose();
            //sf.Dispose();
		}
	}
}
