using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class GeminiClient : MonoBehaviour
{
    public string apiKey;

    public IEnumerator SendImage(Texture2D image, System.Action<string> callback)
    {
        byte[] imageBytes = image.EncodeToPNG();
        string base64 = System.Convert.ToBase64String(imageBytes);

        string json = @"
        {
          ""contents"": [{
            ""parts"": [
              { ""text"": ""Describe this image in under 150 characters"" },
              {
                ""inline_data"": {
                  ""mime_type"": ""image/png"",
                  ""data"": """ + base64 + @"""
                }
              }
            ]
          }]
        }";

        UnityWebRequest req = new UnityWebRequest(
            "https://generativelanguage.googleapis.com/v1/models/gemini-pro-vision:generateContent?key=" + apiKey,
            "POST"
        );

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            callback?.Invoke(req.downloadHandler.text);
        else
            callback?.Invoke(null);
    }
}