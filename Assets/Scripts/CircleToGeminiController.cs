using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class CircleToGeminiController : MonoBehaviour
{
    public CircleToPassthroughCapture capture;
    public GeminiClient gemini;

    public TextMeshProUGUI resultText;
    public Image previewImage;
    public Camera mainCamera;

    private bool isProcessing = false;

    void Start()
    {
        if (resultText != null)
            resultText.text = "Circle something";
    }

    void Update()
    {
        if (resultText != null && mainCamera != null)
        {
            resultText.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 2f;
            resultText.transform.rotation = Quaternion.LookRotation(resultText.transform.position - mainCamera.transform.position);
        }

        if (previewImage != null && mainCamera != null)
        {
            previewImage.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 2.2f;
            previewImage.transform.rotation = Quaternion.LookRotation(previewImage.transform.position - mainCamera.transform.position);
        }
    }

    public void ProcessCircle()
    {
        if (isProcessing) return;
        isProcessing = true;

        if (resultText != null)
            resultText.text = "Analyzing...";

        Texture2D tex = capture.CaptureSquare();

        if (tex == null)
        {
            if (resultText != null)
                resultText.text = "No image captured";

            if (previewImage != null)
                previewImage.sprite = null;

            isProcessing = false;
            return;
        }

        if (previewImage != null)
        {
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );

            previewImage.sprite = sprite;
        }

        StartCoroutine(gemini.SendImage(tex, OnResponse));
    }

    void OnResponse(string json)
    {
        string parsed = ExtractText(json);

        if (string.IsNullOrEmpty(parsed))
            resultText.text = "No object detected";
        else
            resultText.text = parsed.Length > 150 ? parsed.Substring(0, 150) : parsed;

        isProcessing = false;
    }

    string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        int index = json.IndexOf("text");
        if (index == -1) return null;

        int start = json.IndexOf(":", index) + 2;
        int end = json.IndexOf("\"", start);

        if (start < 0 || end < 0) return null;

        return json.Substring(start, end - start);
    }
}