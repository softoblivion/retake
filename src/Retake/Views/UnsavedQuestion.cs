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
    /// <summary>The answer to "Notes are not saved".</summary>
    public enum UnsavedChoice { Retry, Discard, Cancel }

    /// <summary>
    /// The question before notes that could not be written leave memory (closing, another video, a restart for an
    /// update): a sheet titled "Notes are not saved" with what happened, the reason of the last failure, and Cancel,
    /// Discard and Retry (the primary action, focused). Esc, the veil and the close glyph are Cancel.
    /// </summary>
    public static class UnsavedQuestion
    {
        public static Task<UnsavedChoice> Ask(OverlayHost overlay, string? reason)
        {
            var answer = new TaskCompletionSource<UnsavedChoice>();
            var body = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            TextBlock what = Typography.Text(Copy.UnsavedBody, Typography.Body, "ink-2");
            what.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(what);
            if (!string.IsNullOrEmpty(reason))
            {
                TextBlock why = Typography.Text(reason!, Typography.Caption, "error-ink");
                why.TextWrapping = TextWrapping.Wrap;
                why.Margin = new Thickness(0, 10, 0, 0);
                AutomationProperties.SetAutomationId(why, "UnsavedReason");
                body.Children.Add(why);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new GlassButton(Copy.UnsavedCancel, null, ButtonKind.Plain, ButtonSize.M) { MinWidth = 88 };
            var discard = new GlassButton(Copy.UnsavedDiscard, null, ButtonKind.Danger, ButtonSize.M) { MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            var retry = new GlassButton(Copy.Retry, null, ButtonKind.Tinted, ButtonSize.M) { MinWidth = 104, Margin = new Thickness(8, 0, 0, 0) };
            Tip.Set(cancel, Copy.UnsavedCancelTip);
            Tip.Set(discard, Copy.UnsavedDiscardTip);
            Tip.Set(retry, Copy.UnsavedRetryTip);
            AutomationProperties.SetAutomationId(cancel, "UnsavedCancel");
            AutomationProperties.SetAutomationId(discard, "UnsavedDiscard");
            AutomationProperties.SetAutomationId(retry, "UnsavedRetry");
            buttons.Children.Add(cancel);
            buttons.Children.Add(discard);
            buttons.Children.Add(retry);
            body.Children.Add(buttons);
            var sheet = new Sheet(Copy.UnsavedTitle, body, Copy.UnsavedCancelTip, 480);
            AutomationProperties.SetAutomationId(sheet, "UnsavedQuestion");

            Action<bool>? watch = null;
            void Done(UnsavedChoice choice)
            {
                if (watch != null) overlay.SheetChanged -= watch;
                watch = null;
                if (!answer.TrySetResult(choice)) return;
                if (overlay.CurrentSheet == sheet) overlay.CloseSheet();
            }
            cancel.Click += (s, e) => Done(UnsavedChoice.Cancel);
            discard.Click += (s, e) => Done(UnsavedChoice.Discard);
            retry.Click += (s, e) => Done(UnsavedChoice.Retry);
            // Esc, the veil, the close glyph or another sheet: the question was not answered, so nothing leaves.
            watch = open => { if (overlay.CurrentSheet != sheet && sheet.Parent != null) Done(UnsavedChoice.Cancel); };
            overlay.SheetChanged += watch;
            overlay.OpenSheet(sheet, null);
            overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (overlay.CurrentSheet == sheet) retry.Focus(); }));
            return answer.Task;
        }
    }
}
