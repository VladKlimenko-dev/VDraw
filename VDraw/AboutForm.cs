using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace VDraw
{
	public partial class AboutForm : Form
	{
		public AboutForm()
		{
			InitializeComponent();
			WinFormsTheme.Register(this);
			Localization.Register(this);
			ShowAboutText();
		}

		/// <summary>
		/// Текст «о программе» собирается в коде, а не берётся из ресурсов
		/// формы.
		///
		/// В ресурсах он лежал одним английским куском, вместе с номером
		/// версии и датой. Перевести такой кусок можно только целиком,
		/// и тогда перевод оказался бы привязан к номеру версии: сменился
		/// номер — и ключ в словаре больше не находится, окно молча
		/// возвращается к английскому. Собранный из частей, текст
		/// переводится по подписям, а версию и дату берёт у самой сборки.
		/// </summary>
		private void ShowAboutText()
		{
			var text = new StringBuilder();

			text.AppendLine("VDraw");
			text.AppendLine();
			text.AppendLine(L.T("Version {0}", AppInfo.Version));

			if (AppInfo.Build.Length > 0)
				text.AppendLine(L.T("Build date: {0}", AppInfo.Build));

			text.AppendLine();
			text.AppendLine(L.T("Author: {0}", "Vladislav Klimenko"));
			text.AppendLine();
			text.AppendLine(L.T("Description:"));
			text.AppendLine(L.T(
				"This computer program was created for 2D vector graphics, "
				+ "image editing and animation."));
			text.AppendLine();
			text.Append(L.T(
				"Any questions or suggestion for improvement can be sent "
				+ "to email address: {0}",
				"support@line-project.ru"));

			richTextBox1.Text = text.ToString();
		}
	}
}
