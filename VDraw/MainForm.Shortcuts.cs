using VDraw.Editing;

namespace VDraw;

/// <summary>
/// Разбор сочетаний клавиш.
///
/// Прежде сочетания лежали в шести switch прямо здесь, метками case.
/// Настраиваемыми они стать не могли: метка case — константа времени
/// компиляции, а выбранное пользователем сочетание известно только
/// в рантайме. Теперь клавиши живут в Shortcuts, а здесь остаётся
/// таблица «действие — что сделать».
/// </summary>
public partial class MainForm
{
	private Dictionary<string, Action>? _shortcutHandlers;

	/// <summary>
	/// Режим, действия которого разбираются первыми.
	///
	/// Правка узлов — единственный режим со своим набором клавиш.
	/// Режим изображения своего набора не имеет: там работают те же
	/// действия фигур, что и обычно, — так было и до появления реестра.
	/// </summary>
	private static ShortcutScope CurrentShortcutScope
		=> Mode == EditorMode.Curve ? ShortcutScope.Curve : ShortcutScope.Shapes;

	/// <summary>
	/// Разбор сочетания. true — клавиша обработана и дальше не идёт.
	///
	/// Порядок просмотра: сначала набор текущего режима, затем общий
	/// для фигур, затем общий для всего приложения. Благодаря этому
	/// цифра 1 при правке узлов меняет тип узла, а в остальных режимах
	/// остаётся свободной.
	/// </summary>
	private bool TryHandleShortcut(Keys keyData)
	{
		if (_mainView == null || cur_doc == null)
			return false;

		// Клавиши холста отдаются холсту целиком.
		if (IsCanvasFocused() && BelongsToCanvas(keyData))
			return false;

		var action = FindShortcutAction(keyData);

		if (action == null)
			return false;

		// Пока идёт набор текста, поле забирает клавиатуру себе.
		// Пропускаются только команды документа: Ctrl+S во время правки
		// подписи должен сохранять документ, а не вставлять символ.
		if (!action.AllowedWhileTyping && IsTextEntryFocused())
			return false;

		if (!ShortcutHandlers.TryGetValue(action.Id, out var handler))
			return false;

		handler();

		return true;
	}

	private static ShortcutAction? FindShortcutAction(Keys keyData)
	{
		var scope = CurrentShortcutScope;

		var action = Shortcuts.ResolveInScope(keyData, scope);

		if (action == null && scope != ShortcutScope.Shapes)
			action = Shortcuts.ResolveInScope(keyData, ShortcutScope.Shapes);

		return action ?? Shortcuts.ResolveInScope(keyData, ShortcutScope.Global);
	}

	private Dictionary<string, Action> ShortcutHandlers
		=> _shortcutHandlers ??= BuildShortcutHandlers();

	/// <summary>
	/// Что делает каждое действие.
	///
	/// Таблица строится один раз и живёт вместе с формой: обработчики
	/// ссылаются на её поля, и пересобирать её не от чего — меняются
	/// только клавиши, а они здесь не упоминаются вовсе.
	/// </summary>
	private Dictionary<string, Action> BuildShortcutHandlers()
	{
		void Click(EventHandler handler) => handler(this, EventArgs.Empty);

		return new Dictionary<string, Action>(StringComparer.Ordinal)
		{
			["file.new"] = () => Click(d_new_but_Click),
			["file.open"] = () => Click(d_open_but_Click),
			["file.save"] = () => Click(d_save_but_Click),
			["file.saveAs"] = () => Click(d_save_as_but_Click),
			["file.import"] = () => Click(d_import_but_Click),
			["file.export"] = () => Click(d_export_but_Click),
			["file.print"] = () => Click(d_print_but_Click),
			["file.help"] = () => Click(help_but_Click),

			["edit.undo"] = () => Click(undo_but_Click),
			["edit.redo"] = () => Click(redo_but_Click),
			["edit.copyFormat"] = CopyShapeFormat,
			["edit.pasteFormat"] = PasteShapeFormat,
			["edit.selectAll"] = () => _mainView.SelectAllShapes(),
			["edit.delete"] = DeleteSelectedShapes,

			["tool.select"] = () => Click(s_cursor_but_Click),
			["tool.line"] = () => Click(s_new_line_but_Click),
			["tool.curve"] = () => Click(s_new_curve_but_Click),
			["tool.text"] = () => Click(s_new_text_but_Click),
			["tool.measureDistance"] = () => Click(measure_distance_but_Click),
			["tool.nodes"] = () => Click(s_curve_edit_but_Click),
			["tool.polygon"] = () => Click(s_new_poly_but_Click),
			["tool.freePolygon"] = () => Click(s_new_free_poly_but_Click),
			["tool.cutLine"] = () => Click(s_scissors_line_but_Click),
			["tool.cutCurve"] = () => Click(s_scissors_curve_but_Click),

			// У прямоугольника, окружности и эллипса кнопка составная:
			// клавиша включает тот вариант, что выбран в её списке
			// последним. Так же ведёт себя щелчок по самой кнопке.
			["tool.rectangle"] = () => _mainView.SetTool(_selectedRectangleTool),
			["tool.circle"] = () => _mainView.SetTool(_selectedCircleTool),
			["tool.ellipse"] = () => _mainView.SetTool(_selectedEllipseTool),

			["tool.image"] = () => Click(s_new_image_but_Click),
			["tool.imageMode"] = () => Click(s_image_mode_but_Click),

			["arrange.group"] = () => Click(s_group_but_Click),
			["arrange.ungroup"] = () => Click(s_ungroup_but_Click),
			["arrange.bringToFront"] = () => Click(s_bring_to_front_but_Click),
			["arrange.sendToBack"] = () => Click(s_send_back_but_Click),
			["arrange.flipHorizontal"] = () => Click(flip_horizontal_but_Click),
			["arrange.flipVertical"] = () => Click(flip_vertical_but_Click),
			["arrange.rotateCW"] = () => Click(rotate_CW_90_but_Click),
			["arrange.rotateCCW"] = () => Click(rotate_CCW_90_but_Click),

			["bool.union"] = () => Click(s_union_but_Click),
			["bool.subtract"] = () => Click(s_substract_but_Click),
			["bool.intersect"] = () => Click(s_intersect_but_Click),
			["bool.exclude"] = () => Click(s_exclude_xor_but_Click),
			["bool.combine"] = () => Click(s_combine_but_Click),
			["bool.convertToCurve"] = () => Click(s_convert_to_curve_but_Click),

			["nudge.left"] = () => NudgeSelection(-NudgeStep, 0f),
			["nudge.right"] = () => NudgeSelection(NudgeStep, 0f),
			["nudge.up"] = () => NudgeSelection(0f, -NudgeStep),
			["nudge.down"] = () => NudgeSelection(0f, NudgeStep),
			["nudge.leftBig"] = () => NudgeSelection(-NudgeStepLarge, 0f),
			["nudge.rightBig"] = () => NudgeSelection(NudgeStepLarge, 0f),
			["nudge.upBig"] = () => NudgeSelection(0f, -NudgeStepLarge),
			["nudge.downBig"] = () => NudgeSelection(0f, NudgeStepLarge),

			["node.straight"] = () => Click(nodes_straight_but_Click),
			["node.tangent"] = () => Click(node_tangent_but_Click),
			["node.sharp"] = () => Click(node_sharp_but_Click),
			["node.merge"] = () => Click(nodes_merge_but_Click),
			["node.split"] = () => Click(nodes_split_but_Click),

			["view.zoomAll"] = () => Click(v_scale_all_but_Click),
			["view.zoomPage"] = () => Click(v_scale_page_but_Click),
			["view.zoomSelection"] = () => Click(v_scale_selected_but_Click),
			["view.rulers"] = () => Click(v_show_rulers_but_Click),
			["view.guides"] = () => Click(s_show_guidelines_but_Click),
			["view.grid"] = () => Click(page_grid_but_Click),
			["view.snapObjects"] = () => Click(v_snap_but_Click),
			["view.dynamicGuides"] = () => Click(v_dynamic_guidelines_but_Click)
		};
	}
}
