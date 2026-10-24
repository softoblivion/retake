using System.Windows;
using System.Windows.Controls;
using Retake.Design;
using Retake.Design.Controls;
using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The status row of the Updates group of Settings: the status line ("You have the latest version", "Version
    /// 2.0.1 is available"), the time of the last check beneath it, and the next action on the right (Check for
    /// updates, Download, the percentage while downloading, Restart to update). The row under it, the automatic
    /// download switch, is the design layer's own and saves through the core's setting.
    /// </summary>
    internal sealed class UpdateRows
    {
        internal readonly FrameworkElement Element;
        readonly TextBlock status = Typography.Text("", Typography.Label);
        readonly TextBlock checkedAt = Typography.Text("", Typography.Caption, "ink-3");
        readonly ActionButton action;

        internal UpdateRows(UpdatePresenter presenter)
        {
            action = new ActionButton(ButtonSize.S, 116, presenter.Run);
            Element = SettingsSheet.Row(status, checkedAt, action);
            Show(presenter.View);
        }

        internal void Show(UpdateView view)
        {
            PanelView v = view.Panel;
            status.Text = v.StatusLine;
            checkedAt.Text = v.CheckedAt ?? "";
            checkedAt.Visibility = v.CheckedAt != null ? Visibility.Visible : Visibility.Collapsed;
            action.Show(v.RowAction);
        }
    }
}
