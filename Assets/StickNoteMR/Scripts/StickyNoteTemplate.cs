using UnityEngine;

public class StickyNoteTemplate : MonoBehaviour
{
    [SerializeField] private string templateName = "Yellow Square";
    [SerializeField] private string shortLabel = "Idea";
    [SerializeField] private Vector2 noteSizeMeters = new Vector2(0.12f, 0.12f);
    [SerializeField] private Color noteColor = new Color(0.95f, 0.88f, 0.47f, 1f);
    [SerializeField] private Color frameColor = new Color(0.74f, 0.48f, 0.18f, 1f);

    public string TemplateName => templateName;
    public string ShortLabel => shortLabel;
    public Vector2 NoteSizeMeters => noteSizeMeters;
    public Color NoteColor => noteColor;
    public Color FrameColor => frameColor;

    public void Configure(string noteTemplateName, string noteLabel, Vector2 sizeMeters, Color faceColor, Color borderColor)
    {
        templateName = noteTemplateName;
        shortLabel = noteLabel;
        noteSizeMeters = sizeMeters;
        noteColor = faceColor;
        frameColor = borderColor;
    }
}
