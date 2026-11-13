using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The question before Delete notes removes files for good, asked when the Recycle Bin cannot take them: a sheet
    /// titled "Delete notes permanently?" with what would go and the folder it is in, and Cancel (focused) and Delete
    /// permanently. Esc, the veil and the close glyph are Cancel; only the Delete permanently button answers yes.
    /// </summary>
    public static class DeleteQuestion
    {
        public static Task<bool> Ask(OverlayHost overlay, string text)
        {
            var answer = new TaskCompletionSource<bool>();
            Sheet sheet = Build(text, out GlassButton cancel, out GlassButton delete);

            Action<bool>? watch = null;
            void Done(bool permanently)
            {
                if (watch != null) overlay.SheetChanged -= watch;
                watch = null;
                if (!answer.TrySetResult(permanently)) return;
                if (overlay.CurrentSheet == sheet) overlay.CloseSheet();
            }
            cancel.Click += (s, e) => Done(false);
            delete.Click += (s, e) => Done(true);
            // Esc, the veil, the close glyph or another sheet: the question was not answered, so nothing is deleted.
            watch = open => { if (overlay.CurrentSheet != sheet && sheet.Parent != null) Done(false); };
            overlay.SheetChanged += watch;
            overlay.OpenSheet(sheet, null);
            overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (overlay.CurrentSheet == sheet) cancel.Focus(); }));
            return answer.Task;
        }

        /// <summary>The sheet itself, also shown by the gallery.</summary>
        public static Sheet Build(string text, out GlassButton cancel, out GlassButton delete)
        {
            var body = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            TextBlock what = Typography.Text(text, Typography.Body, "ink-2");
            what.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(what, "DeletePermanentText");
            body.Children.Add(what);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            cancel = new GlassButton(Copy.DeleteCancel, null, ButtonKind.Plain, ButtonSize.M) { MinWidth = 88 };
            delete = new GlassButton(Copy.DeletePermanent, null, ButtonKind.Danger, ButtonSize.M) { MinWidth = 104, Margin = new Thickness(8, 0, 0, 0) };
            Tip.Set(cancel, Copy.DeleteCancelTip);
            Tip.Set(delete, Copy.DeletePermanentTip);
            AutomationProperties.SetAutomationId(cancel, "DeleteCancel");
            AutomationProperties.SetAutomationId(delete, "DeletePermanently");
            buttons.Children.Add(cancel);
            buttons.Children.Add(delete);
            body.Children.Add(buttons);
            var sheet = new Sheet(Copy.DeletePermanentTitle, body, Copy.DeleteCancelTip, 480);
            AutomationProperties.SetAutomationId(sheet, "DeleteQuestion");
            return sheet;
        }
    }
}
