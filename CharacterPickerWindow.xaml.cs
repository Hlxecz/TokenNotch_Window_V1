using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TokenNotchWin;

public partial class CharacterPickerWindow : Window
{
    private static readonly (PetCharacter Pet, string Label, string Category)[] Choices =
    [
        (PetCharacter.PixelCharmander, "파이리", "진화"),
        (PetCharacter.PixelPikachu, "피카츄", ""),
        (PetCharacter.PixelBulbasaur, "이상해씨", "진화"),
        (PetCharacter.PixelSquirtle, "꼬부기", "진화"),
        (PetCharacter.Ditto, "메타몽", ""),
        (PetCharacter.Snorlax, "잠만보", ""),
        (PetCharacter.Arceus, "아르세우스", "전설"),
        (PetCharacter.Dialga, "디아루가", "전설"),
        (PetCharacter.Palkia, "펄기아", "전설"),
        (PetCharacter.Giratina, "기라티나", "전설"),
        (PetCharacter.Mewtwo, "뮤츠", "전설"),
        (PetCharacter.Lugia, "루기아", "전설"),
        (PetCharacter.Kyogre, "가이오가", "전설"),
        (PetCharacter.Groudon, "그란돈", "전설"),
        (PetCharacter.Rayquaza, "레쿠쟈", "전설"),
        (PetCharacter.Clawd, "Clawd", "기본"),
    ];

    public event Action<PetCharacter>? CharacterSelected;

    public CharacterPickerWindow(PetCharacter selected, Stage stage)
    {
        InitializeComponent();

        foreach (var (pet, label, category) in Choices)
        {
            if (!PokemonSpriteAtlas.IsAvailable(pet)) continue;
            PetGrid.Children.Add(BuildTile(pet, label, category, selected, stage));
        }
    }

    private Button BuildTile(PetCharacter pet, string label, string category,
        PetCharacter selected, Stage stage)
    {
        var preview = new Grid { Width = 82, Height = 68, IsHitTestVisible = false };
        if (pet == PetCharacter.Clawd)
        {
            preview.Children.Add(new ClawdControl
            {
                Scale = 1.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            });
        }
        else
        {
            preview.Children.Add(new PokemonPetControl
            {
                Character = pet,
                Stage = stage,
                Scale = 0.56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            });
        }

        if (pet == selected)
        {
            preview.Children.Add(new TextBlock
            {
                Text = "✓",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 217, 102)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 3, 0),
            });
        }

        var text = new Grid { Margin = new Thickness(2, 1, 2, 0) };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(category))
        {
            text.Children.Add(new TextBlock
            {
                Text = category,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var content = new StackPanel();
        content.Children.Add(preview);
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            Tag = pet,
            Style = (Style)FindResource("PetTileStyle"),
            ToolTip = $"{label} 선택",
        };
        if (pet == selected)
        {
            button.Background = new SolidColorBrush(Color.FromArgb(0x25, 255, 217, 102));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 217, 102));
        }
        button.Click += (_, _) =>
        {
            CharacterSelected?.Invoke(pet);
            Close();
        };
        return button;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
