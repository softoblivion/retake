using System.Windows;
using System.Windows.Markup;

namespace Retake.Design
{
    /// <summary>Starts the design layer on an application: loads the themes, applies the chosen one, adds the shared styles.</summary>
    public static class Look
    {
        public static void Init(Application app, Prefs prefs)
        {
            Themes.Load();
            if (!Themes.IsKnown(prefs.Theme)) prefs.Theme = Themes.DefaultName;
            app.Resources.MergedDictionaries.Add(Styles());
            Themes.Apply(prefs.Theme, app.Resources);
        }

        /// <summary>The thin scroll bar of the kit: a 10 px lane with a rounded --scroll-thumb, no arrows.</summary>
        static ResourceDictionary Styles()
        {
            const string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='MinWidth' Value='10'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Track x:Name='PART_Track' IsDirectionReversed='True'>
    <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'>
     <Border x:Name='grip' Background='{DynamicResource T.scroll-thumb}' CornerRadius='2' Margin='3,0'/>
     <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='grip' Property='Background' Value='{DynamicResource T.scroll-thumb-hover}'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
   </Track>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Visibility' Value='Collapsed'/></Trigger></Style.Triggers>
 </Style>
</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }
    }
}
