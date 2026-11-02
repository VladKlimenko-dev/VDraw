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
		AvDropDownBox? view = null;
		bool syncing = false;

		void Pull()
		{
			if (view == null)
				return;

			syncing = true;

			try
			{
				// Состав списка перечитывается вместе с выбором: он
				// меняется из кода (форматы листа, единицы), а события
				// об этом ComboBox не поднимает, и привязаться больше
				// не к чему.
				var items = new List<string>(source.Items.Count);

				foreach (object? item in source.Items)
					items.Add(item?.ToString() ?? string.Empty);

				view.SetItems(items);
				view.SetSelected(source.SelectedItem?.ToString() ?? string.Empty);
				view.Enabled = source.Enabled;
			}
			finally
			{
				syncing = false;
			}
		}

		global::Avalonia.Controls.Control Create()
		{
			var created = new AvDropDownBox
			{
				ItemHeight = 26f,
				DropDownMaxHeight = 320f,
				HorizontalAlignment = HorizontalAlignment.Stretch,

				// Растягивается на всю высоту хоста, а своя нижняя
				// граница снимается. Иначе список берёт высоту у темы
				// Fluent — она больше отведённых полосой точек, — и его
				// верхняя с нижней границы оказывались срезаны: рамка
				// выглядела состоящей из двух боковых палочек.
				VerticalAlignment = VerticalAlignment.Stretch,
				MinHeight = 0
			};

			created.SelectedIndexChanged += (index, _) =>
			{
				if (syncing || index < 0 || index >= source.Items.Count)
					return;

				source.SelectedIndex = index;
			};

			view = created;

			Pull();

			return created;
		}

		source.SelectedIndexChanged += (_, _) => Pull();
		source.EnabledChanged += (_, _) => Pull();

		Mount(source, Create);
	}

	#endregion

	#region Счётчики

	private static void Replace(NumericUpDown source)
	{
		AvNumericUpDown? view = null;
		bool syncing = false;

		void Pull()
		{
			if (view == null)
				return;

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
				view.AllowDecimal = source.DecimalPlaces > 0;

				view.SetValue((float)source.Value);
				view.Enabled = source.Enabled;
			}
			finally
			{
				syncing = false;
			}
		}

		global::Avalonia.Controls.Control Create()
		{
			var created = new AvNumericUpDown
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				Height = double.NaN,
				MinHeight = 0
			};

			created.ValueChanged += value =>
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

			view = created;

			Pull();

			return created;
		}

		source.ValueChanged += (_, _) => Pull();
		source.EnabledChanged += (_, _) => Pull();

		Mount(source, Create);
	}

	#endregion

	#region Флажки

	private static void Replace(CheckBox source)
	{
		AvCheckBox? view = null;
		bool syncing = false;

		void Pull()
		{
			if (view == null)
				return;

			syncing = true;

			try
			{
				view.Text = source.Text;
				view.Checked = source.Checked;
				view.Enabled = source.Enabled;
			}
			finally
			{
				syncing = false;
			}
		}

		global::Avalonia.Controls.Control Create()
		{
			var created = new AvCheckBox
			{
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center
			};

			created.CheckedChanged += value =>
			{
				if (syncing)
					return;

				source.Checked = value;
			};

			view = created;

			Pull();

			return created;
		}

		source.CheckedChanged += (_, _) => Pull();
		source.EnabledChanged += (_, _) => Pull();
		source.TextChanged += (_, _) => Pull();

		Mount(source, Create);
	}

	#endregion

	#region Размещение

	/// <summary>
	/// Ставит представление на место модели и прячет её.
	///
	/// Место занимается тем же индексом в коллекции родителя: полосы
	/// инструментов — это FlowLayoutPanel, и порядок детей там определяет
	/// порядок на экране. Спрятанная модель из раскладки выпадает, так что
	/// ряд не разъезжается.
	///
	/// Представление пересоздаётся при каждой смене темы. Кисти Avalonia
	/// читаются в момент создания контрола (см. AvTheme) — панель свойств
	/// по той же причине пересобирается целиком. Контрол, созданный один
	/// раз, остался бы в цветах темы, которая была при запуске: в светлой
	/// теме поля ввода так и стояли чёрными.
	/// </summary>
	private static void Mount(Control source, Func<global::Avalonia.Controls.Control> create)
	{
		var parent = source.Parent;

		if (parent == null)
			return;

		int index = parent.Controls.GetChildIndex(source);

		var host = new global::Avalonia.Win32.Interoperability.WinFormsAvaloniaControlHost
		{
			Content = Wrap(create()),
			Location = source.Location,
			Size = source.Size,
			Margin = source.Margin,
			Anchor = source.Anchor,
			TabStop = false
		};

		// Размер модели всё ещё ведущий: его задаёт дизайнер, и полоса
		// при смене разрешения экрана пересчитывает его сама.
		source.SizeChanged += (_, _) => host.Size = source.Size;

		source.Visible = false;

		parent.Controls.Add(host);
		parent.Controls.SetChildIndex(host, index);

		SkUiTheme.ThemeChanged += () => host.Content = Wrap(create());
	}

	/// <summary>
	/// Подкладывает под контрол фон темы.
	///
	/// Без подложки видно фон окна Avalonia, а он берётся у темы Fluent
	/// по варианту оформления: белый в светлой, почти чёрный в тёмной.
	/// На полосе инструментов это читалось как прямоугольник чужого
	/// цвета вокруг флажка.
	/// </summary>
	private static global::Avalonia.Controls.Control Wrap(global::Avalonia.Controls.Control view)
		=> new global::Avalonia.Controls.Border
		{
			Background = AvTheme.PanelBack,
			Child = view
		};


	#endregion
}
