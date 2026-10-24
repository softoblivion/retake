using System.Windows;
using System.Windows.Automation.Peers;

namespace Retake.Views
{
    /// <summary>
    /// Makes a panel of the views visible to UI Automation as a pane with its automation id, name and item status
    /// (layout panels have no automation peer of their own).
    /// </summary>
    public sealed class PanePeer : FrameworkElementAutomationPeer
    {
        public PanePeer(FrameworkElement owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => Owner.GetType().Name;
        protected override bool IsControlElementCore() => true;
    }
}
