using UnityEngine;

public class StickNoteMRActionButton : MonoBehaviour
{
    public enum ActionKind
    {
        PreviousPage,
        NextPage,
        UseHighlight,
        UsePen,
        UseAirInk,
        ClearCurrentPage,
        ResetBoard
    }

    [SerializeField] private ActionKind action;
    [SerializeField] private Renderer buttonRenderer;
    [SerializeField] private TextMesh labelText;
    [SerializeField] private Color normalColor = new Color(0.30f, 0.35f, 0.43f, 0.92f);
    [SerializeField] private Color selectedColor = new Color(0.97f, 0.80f, 0.21f, 0.96f);

    public ActionKind Action => action;

    public void Configure(ActionKind actionKind, string label, Renderer renderer, TextMesh textMesh)
    {
        action = actionKind;
        buttonRenderer = renderer;
        labelText = textMesh;

        if (labelText != null)
        {
            labelText.text = label;
        }

        SetSelected(false);
    }

    public void SetSelected(bool isSelected)
    {
        if (buttonRenderer != null)
        {
            buttonRenderer.material.color = isSelected ? selectedColor : normalColor;
        }

        transform.localScale = isSelected ? Vector3.one * 1.06f : Vector3.one;
    }
}
