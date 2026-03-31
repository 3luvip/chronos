using Godot;

#nullable enable

// Attach this script to a Control root in a UI scene.
// Expected child nodes:
// - TitleLabel (Label)
// - PlayButton (Button)
// - NewsText (RichTextLabel)
public partial class FontSystemExample : Control
{
    public override void _Ready()
    {
        var fontSystem = FontSystem.Instance;
        if (fontSystem is null)
        {
            GD.PushWarning("FontSystemExample: FontSystem autoload is missing.");
            return;
        }

        var title = GetNodeOrNull<Label>("TitleLabel");
        var playButton = GetNodeOrNull<Button>("PlayButton");
        var news = GetNodeOrNull<RichTextLabel>("NewsText");

        if (title is not null)
        {
            fontSystem.ApplyFont(title, "NotoSans:Bold", 42);
            title.Text = "Chronos Online";
        }

        if (playButton is not null)
        {
            fontSystem.ApplyFont(playButton, "NotoSans", 24);
            playButton.Text = "Play";
        }

        if (news is not null)
        {
            fontSystem.ApplyFont(news, "NotoSans", 20);
            news.Text = "Xin chao Viet Nam - Unicode fallback san sang.";
        }
    }
}

