using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Список шрифтов системы и кэш гарнитур.
///
/// Гарнитуры создаются по требованию и живут до конца работы приложения:
/// SKTypeface для каждого шрифта из трёхсот, созданный заранее, занял бы
/// десятки мегабайт, а видимы одновременно семь строк списка.
/// </summary>
public static class SystemFontCatalog
{
	private static List<string>? _families;

	private static readonly Dictionary<string, SKTypeface?> _typefaces =
		new(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<(string Family, float Size), SKFont> _fonts = new();

	private static readonly Dictionary<string, bool> _canRenderOwnName =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Семейства шрифтов системы, по алфавиту, без дубликатов.</summary>
	public static IReadOnlyList<string> Families => _families ??= LoadFamilies();

	/// <summary>
	/// Сбрасывает список семейств.
	///
	/// Вызывается после импорта PDF: список кэшируется на всё время работы
	/// приложения, и без сброса поднятые из файла шрифты не появились бы
	/// в выпадающем списке до перезапуска.
	/// </summary>
	public static void InvalidateFamilies() => _families = null;

	private static List<string> LoadFamilies()
	{
		var result = new List<string>();

		try
		{
			var families = SKFontManager.Default.GetFontFamilies();

			if (families != null)
			{
				result.AddRange(families
					.Where(f => !string.IsNullOrWhiteSpace(f))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase));
			}
		}
		catch
		{
			// Перечисление шрифтов не должно ронять запуск приложения.
		}

		// Шрифты, поднятые из импортированных PDF, в системе не
		// зарегистрированы, но выбирать их в списке пользователь должен
		// наравне с остальными.
		foreach (string family in EmbeddedFontRegistry.Families)
		{
			if (!result.Contains(family, StringComparer.OrdinalIgnoreCase))
				result.Add(family);
		}

		result.Sort(StringComparer.CurrentCultureIgnoreCase);

		if (result.Count == 0)
			result.Add("Arial");

		return result;
	}

	public static SKTypeface? GetTypeface(string? family)
	{
		if (string.IsNullOrWhiteSpace(family))
			return null;

		if (_typefaces.TryGetValue(family, out var cached))
			return cached;

		// Шрифт, поднятый из PDF, в системе не установлен: FromFamilyName
		// его не найдёт и молча вернёт замену.
		var embedded = EmbeddedFontRegistry.Resolve(family, false, false);

		if (embedded != null)
		{
			_typefaces[family] = embedded;
			return embedded;
		}

		SKTypeface? typeface = null;

		try
		{
			typeface = SKTypeface.FromFamilyName(family);
		}
		catch
		{
		}

		_typefaces[family] = typeface;
		return typeface;
	}

	/// <summary>Шрифт заданного размера. Кэшируется, чтобы не создавать его каждый кадр.</summary>
	public static SKFont? GetFont(string? family, float size)
	{
		if (string.IsNullOrWhiteSpace(family))
			return null;

		var key = (family, size);

		if (_fonts.TryGetValue(key, out var cached))
			return cached;

		var typeface = GetTypeface(family);
		if (typeface == null)
			return null;

		var font = new SKFont(typeface, size);
		_fonts[key] = font;
		return font;
	}

	/// <summary>
	/// Может ли шрифт отрисовать собственное название.
	///
	/// Символьные шрифты (Wingdings, Symbol) вместо названия покажут набор
	/// значков, а латинский шрифт не покажет кириллическое название. В таких
	/// случаях строка рисуется шрифтом интерфейса, иначе пользователь не
	/// прочтёт, что он выбирает.
	/// </summary>
	public static bool CanRenderOwnName(string family)
	{
		if (string.IsNullOrWhiteSpace(family))
			return false;

		if (_canRenderOwnName.TryGetValue(family, out bool cached))
			return cached;

		bool result = true;

		try
		{
			var typeface = GetTypeface(family);

			if (typeface == null)
			{
				result = false;
			}
			else
			{
				var glyphs = typeface.GetGlyphs(family);

				// Нулевой идентификатор глифа означает, что символа в шрифте нет.
				result = glyphs != null && glyphs.Length > 0 && glyphs.All(g => g != 0);
			}
		}
		catch
		{
			// Различия в API между версиями SkiaSharp не должны ломать список:
			// в сомнительном случае считаем, что шрифт название нарисует.
			result = true;
		}

		_canRenderOwnName[family] = result;
		return result;
	}
}