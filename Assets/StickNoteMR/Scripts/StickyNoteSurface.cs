using UnityEngine;

public class StickyNoteSurface : MonoBehaviour
{
    [SerializeField] private StickyNoteTemplate template;
    [SerializeField] private InkCanvas inkCanvas;
    [SerializeField] private TextMesh titleText;
    [SerializeField] private int pageIndex = 1;

    public StickyNoteTemplate Template => template;
    public InkCanvas InkCanvas => inkCanvas;
    public int PageIndex => pageIndex;

    public void Configure(StickyNoteTemplate sourceTemplate, InkCanvas surfaceInkCanvas, TextMesh labelText)
    {
        template = sourceTemplate;
        inkCanvas = surfaceInkCanvas;
        titleText = labelText;

        if (titleText != null && template != null)
        {
            titleText.text = template.ShortLabel;
        }
    }

    public void SetPageIndex(int index)
    {
        pageIndex = Mathf.Max(1, index);
    }

    public void SetVisible(bool isVisible)
    {
        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }
    }

    public void ClearInk()
    {
        inkCanvas?.ClearAll();
    }
}
