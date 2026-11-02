using Avalonia.Layout;

using SkiaSharp;

using VDraw.Ui;

using Control = System.Windows.Forms.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace VDraw.Ui.Avalon;

/// <summary>
/// Подмена стандартных контролов на полосах инструментов контролами
/// Avalonia.
///
/// Зачем. Списки и счётчики WinForms в тёмной теме остаются светлыми
/// там, куда стили формы не достают: раскрытый список рисует отдельное
/// окно, а стрелки счётчика — система. Перекрасить их можно только
/// отрисовкой владельцем, и это была бы вторая реализация контрола
/// рядом с той, что уже написана для панели свойств.
///
/// Как. Исходный контрол не удаляется, а прячется и остаётся моделью:
/// в нём по-прежнему лежат значение, список и признак доступности,
/// на него по-прежнему подписан весь код формы, и файл дизайнера
/// править не нужно — его пользователь перегенерирует сам. Рядом,
/// на то же место, встаёт контрол Avalonia как представление. Правка
/// в представлении кладёт значение в модель, и обработчики формы
/// срабатывают ровно так же, как от руки пользователя; изменение
/// модели из кода возвращается в представление.
///
/// Почему не наоборот — не выбросить исходный и не переписать код
/// на Avalonia: обработчиков у этих четырнадцати контролов несколько
/// десятков, и они разбросаны по MainForm вперемежку с отключением
/// и обратной подпиской. Подмена представления оставляет всю эту
/// логику нетронутой.
/// </summary>
public static class AvToolbarSwap
{
	/// <summary>
	/// Подменяет всё, что стоит на полосах инструментов.
	///
	/// Вызывается после InitializeComponent и после того, как списки
	/// заполнены: представление снимает с модели и значение, и состав
	/// списка.
	/// </summary>
	public static void ReplaceAll(IEnumerable<Control> controls)
	{
		if (!AvBootstrap.IsStarted)
			return;

		// Список материализуется: подмена добавляет контролы в тот же
		// родительский контейнер, а менять коллекцию во время обхода
		// нельзя.
		foreach (var control in controls.ToList())
		{
			switch (control)
			{
				case ComboBox combo:
					Replace(combo);
					break;

				case NumericUpDown nud:
					Replace(nud);
					break;

				case CheckBox check:
					Replace(check);
					break;
			}
		}
	}

	#region Списки

	private static void Replace(ComboBox source)
	{
		var view = new AvDropDownBox
		{
			ItemHeight = 26f,
			DropDownMaxHeight = 320f,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Center
		};

		bool syncing = false;

		void PullItems()
		{
			var items = new List<string>(source.Items.Count);

			foreach (object? item in source.Items)
				items.Add(item?.ToString() ?? string.Empty);

			view.SetItems(items);
		}

		void PullSelection()
		{
			syncing = true;

			try
			{
				// Состав списка перечитывается вместе с выбором: он
				// меняется из кода (форматы листа, единицы), а события
				// об этом ComboBox не поднимает, и привязаться больше
				// не к чему.
				PullItems();

				view.SetSelected(source.SelectedItem?.ToString() ?? string.Empty);
			}
			finally
			{
				syncing = false;
			}
		}

		view.SelectedIndexChanged += (index, _) =>
		{
			if (syncing || index < 0 || index >= source.Items.Count)
				return;

			source.SelectedIndex = index;
		};

		source.SelectedIndexChanged += (_, _) => PullSelection();

		PullSelection();

		Mount(source, view, () => view.Enabled = source.Enabled);
	}

	#endregion

	#region Счётчики

	private static void Replace(NumericUpDown source)
	{
		var view = new AvNumericUpDown
		{
			Minimum = (float)source.Minimum,
			Maximum = (float)source.Maximum,
			Increment = (float)source.Increment,
			Decimals = source.DecimalPlaces,
			AllowNegative = source.Minimum < 0,
			AllowDecimal = source.DecimalPlaces > 0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Center
		};

		bool syncing = false;

		void Pull()
		{
			syncing = true;

			try
			{
				// Границы тоже перечитываются: их задают из кода вместе
				// со значением — например при смене единиц листа.
				view.Minimum = (float)source.Minimum;
				view.Maximum = (float)source.Maximum;
				view.Increment = (float)source.Increment;
				view.Decimals = source.DecimalPlaces;
				view.AllowNegative = source.Minimum < 0;

				view.SetValue((float)source.Value);
			}
			finally
			{
				syncing = false;
			}
		}

		view.ValueChanged += value =>
		{
			if (syncing)
				return;

			decimal clamped = Math.Clamp((decimal)value, source.Minimum, source.Maximum);

			// Сравнение перед записью: присваивание того же значения
			// подняло бы ValueChanged и вернулось бы сюда через Pull,
			// а обработчики формы делают по этому событию настоящую
			// работу — перестраивают лист.
			if (source.Value != clamped)
				source.Value = clamped;
		};

		source.ValueChanged += (_, _) => Pull();

		Pull();

		Mount(source, view, () => view.Enabled = source.Enabled);
	}

	#endregion

	#region Флажки

	private static void Replace(CheckBox source)
	{
		var view = new AvCheckBox
		{
			Text = source.Text,
			Checked = source.Checked,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center
		};

		bool syncing = false;

		view.CheckedChanged += value =>
		{
			if (syncing)
				return;

			source.Checked = value;
		};

		source.CheckedChanged += (_, _) =>
		{
			syncing = true;

			try
			{
				view.Checked = source.Checked;
			}
			finally
			{
				syncing = false;
			}
		};

		source.TextChanged += (_, _) => view.Text = source.Text;

		Mount(source, view, () => view.Enabled = source.Enabled);
	}

	#endregion

	#region Размещение

	/// <summary>
	/// Ставит представление на место модели и прячет её.
	///
	/// Место занимается тем же индексом в коллекции родителя: полосы
	/// инструментов — это FlowLayoutPanel, и порядок детей там
	/// определяет порядок на экране. Спрятанная модель из раскладки
	/// выпадает, так что ряд не разъезжается.
	/// </summary>
	private static void Mount(
		Control source,
		global::Avalonia.Controls.Control view,
		Action applyEnabled)
	{
		var parent = source.Parent;

		if (parent == null)
			return;

		int index = parent.Controls.GetChildIndex(source);

		var host = new global::Avalonia.Win32.Interoperability.WinFormsAvaloniaControlHost
		{
			Content = view,
			Location = source.Location,
			Size = source.Size,
			Margin = source.Margin,
			Anchor = source.Anchor,
			TabStop = false,
			BackColor = ToColor(SkUiTheme.Current.PanelBack)
		};

		applyEnabled();

		source.EnabledChanged += (_, _) => applyEnabled();

		// Размер модели всё ещё ведущий: его задаёт дизайнер, и полоса
		// при смене разрешения экрана пересчитывает его сама.
		source.SizeChanged += (_, _) => host.Size = source.Size;

		source.Visible = false;

		parent.Controls.Add(host);
		parent.Controls.SetChildIndex(host, index);

		SkUiTheme.ThemeChanged += () => host.BackColor = ToColor(SkUiTheme.Current.PanelBack);
	}

	private static Color ToColor(SKColor c)
		=> Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	#endregion
}
