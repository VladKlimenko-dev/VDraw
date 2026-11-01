using Avalonia;
using Avalonia.Themes.Fluent;

using Application = Avalonia.Application;

namespace VDraw.Ui.Avalon;

/// <summary>
/// Приложение Avalonia внутри приложения WinForms.
///
/// Окон у него нет: контролы Avalonia живут внутри WinForms-контейнеров
/// (см. AvPropertyPanelHost). Экземпляр нужен лишь затем, чтобы существовали
/// стили и ресурсы оформления — без запущенного Application любой контрол
/// Avalonia остался бы без темы и упал бы при первой отрисовке.
/// </summary>
public sealed class AvApp : Application
{
	public override void Initialize()
	{
		// Fluent — штатная тема Avalonia. Своё оформление задаётся не вместо
		// неё, а поверх: встроенные TextBox, ComboBox и Slider берут от неё
		// поведение и анимации, а цвета получают из палитры VDraw.
		Styles.Add(new FluentTheme());

		base.Initialize();
	}
}

/// <summary>
/// Однократный запуск Avalonia.
///
/// Вызывается из Program.Main до Application.Run(new MainForm()): к моменту
/// создания формы инфраструктура Avalonia должна быть готова, иначе первый же
/// хост-контрол в конструкторе формы окажется без приложения.
/// </summary>
public static class AvBootstrap
{
	private static bool _started;

	public static bool IsStarted => _started;

	/// <summary>
	/// Причина, по которой запуск не удался. Пустая строка при успехе.
	/// Нужна MainForm: если Avalonia не поднялась, панель свойств должна
	/// честно сказать об этом, а не молча исчезнуть.
	/// </summary>
	public static string FailureReason { get; private set; } = string.Empty;

	public static void EnsureStarted()
	{
		if (_started || FailureReason.Length > 0)
			return;

		try
		{
			AppBuilder.Configure<AvApp>()
				.UsePlatformDetect()
				.SetupWithoutStarting();

			_started = true;

			// Тема выставляется сразу: настройки уже прочитаны, и панель
			// должна появиться в нужном оформлении с первого кадра.
			AvTheme.Apply(SkUiTheme.CurrentKind);

			// Дальше тему переключает тот же SkUiTheme, что и всё остальное
			// приложение — отдельной кнопки у Avalonia нет и не нужно.
			SkUiTheme.ThemeChanged += () => AvTheme.Apply(SkUiTheme.CurrentKind);
		}
		catch (Exception ex)
		{
			FailureReason = ex.Message;
			ErrorLog.Write("Не удалось запустить Avalonia", ex);
		}
	}
}
