namespace VDraw;

/// <summary>
/// Переключение языка интерфейса. Отдельный файл частичного класса:
/// логика не связана ни с одним из существующих разделов MainForm.
/// </summary>
public partial class MainForm
{
	/// <summary>
	/// Вызывается один раз из конструктора, ПОСЛЕ InitializeComponent
	/// и SetToolTips.
	///
	/// Порядок важен: Register запоминает исходные английские тексты меню
	/// и кнопок, а на момент вызова они должны быть теми, что расставил
	/// дизайнер. Если запомнить их после первого перевода, ключом станет
	/// русский текст и вернуться на английский будет уже нечем.
	/// </summary>
	private void InitLanguage()
	{
		Localization.LoadChoice();
		Localization.Register(this);

		Localization.LanguageChanged += ApplyLanguage;

		UpdateLanguageButton();
	}

	private void language_switch_but_Click(object sender, EventArgs e)
	{
		Localization.Toggle();
	}

	/// <summary>
	/// Заново расставляет тексты по всему окну.
	///
	/// Подсказки не хранятся в элементах, а раздаются кодом, поэтому
	/// SetToolTips вызывается целиком: перебирать девяносто вызовов
	/// поодиночке смысла нет, метод дешёвый.
	/// </summary>
	private void ApplyLanguage()
	{
		SuspendLayout();

		try
		{
			Localization.Apply(this);

			SetToolTips();
			UpdateLanguageButton();

			// Панель свойств собирает подписи строк при построении,
			// поэтому её нужно пересобрать, а не перерисовать.
			_propPanel?.RebuildForSelection();

			// Панель страницы читает формат и размеры из самой страницы.
			// Вызов страхует от любого поля, которое могло сбиться при
			// расстановке текстов: показанные значения обязаны совпадать
			// с документом, а не с тем, что осталось в контроле.
			if (CurrentPage != null)
				InitPageData();

			_pageTabs?.Invalidate();
			_mainView?.Invalidate();
		}
		finally
		{
			ResumeLayout(true);
		}

		Invalidate(true);
	}

	/// <summary>
	/// На кнопке — код ТЕКУЩЕГО языка, а не следующего.
	///
	/// Кнопка отвечает на вопрос «на каком языке я сейчас», и это читается
	/// однозначно. Подпись «на что переключит» выглядит так же, но означает
	/// противоположное, и пользователь узнаёт разницу только нажав.
	/// Куда ведёт нажатие — сказано в подсказке.
	/// </summary>
	private void UpdateLanguageButton()
	{
		if (language_switch_but == null)
			return;

		language_switch_but.Text = Localization.CurrentCode;

		string hint = Localization.Current == AppLanguage.Russian
			? "Переключить язык интерфейса\r\nСейчас: русский. Нажмите, чтобы перейти на английский."
			: "Switch interface language\r\nCurrent: English. Click to switch to Russian.";

		toolTip.SetToolTip(language_switch_but, hint);
	}
}