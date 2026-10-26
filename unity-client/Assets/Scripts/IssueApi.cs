using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class IssuePosition
{
    public float x;
    public float y;
    public float z;

    public static IssuePosition From(Vector3 value)
    {
        return new IssuePosition
        {
            x = Mathf.Round(value.x * 1000f) / 1000f,
            y = Mathf.Round(value.y * 1000f) / 1000f,
            z = Mathf.Round(value.z * 1000f) / 1000f
        };
    }
}

[Serializable]
public class IssueCreatePayload
{
    public string title;
    public string description;
    public string priority;
    public IssuePosition position;
}

[Serializable]
public class IssueRecord
{
    public string id;
    public string title;
    public string description;
    public string priority;
    public string status;
    public IssuePosition position;
    public string createdAt;
    public string updatedAt;
}

[Serializable]
public class IssueListResponse
{
    public IssueRecord[] items;
}

[Serializable]
public class ApiError
{
    public string error;
}

public sealed class IssueApi
{
    public const string BaseUrl = "http://127.0.0.1:8080";

    public static IssueCreatePayload BuildPayload(
        string title,
        string description,
        int priorityIndex,
        Vector3 position)
    {
        string[] priorities = { "low", "medium", "high" };
        if (priorityIndex < 0 || priorityIndex >= priorities.Length)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(priorityIndex),
                "priority index must be 0, 1 or 2");
        }

        string normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length == 0)
        {
            throw new System.ArgumentException(
                "title is required",
                nameof(title));
        }

        return new IssueCreatePayload
        {
            title = normalizedTitle,
            description = (description ?? string.Empty).Trim(),
            priority = priorities[priorityIndex],
            position = IssuePosition.From(position)
        };
    }

    public IEnumerator CreateIssue(
        IssueCreatePayload payload,
        Action<long, string, IssueRecord> completed)
    {
        string body = JsonUtility.ToJson(payload);
        using UnityWebRequest request = new UnityWebRequest(
            BaseUrl + "/api/issues",
            UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.timeout = 10;

        yield return request.SendWebRequest();

        IssueRecord issue = null;
        if (request.responseCode == 201)
        {
            try
            {
                issue = JsonUtility.FromJson<IssueRecord>(
                    request.downloadHandler?.text ?? string.Empty);
            }
            catch (ArgumentException)
            {
                // The successful status still decides success; details are optional.
            }
        }
        completed?.Invoke(
            request.responseCode,
            ReadResponse(request),
            issue);
    }

    public IEnumerator FetchIssues(
        Action<long, string, IssueRecord[]> completed)
    {
        using UnityWebRequest request = UnityWebRequest.Get(
            BaseUrl + "/api/issues");
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Accept", "application/json");
        request.timeout = 10;

        yield return request.SendWebRequest();

        IssueRecord[] issues = null;
        if (request.responseCode == 200)
        {
            string responseText = request.downloadHandler?.text ?? string.Empty;
            try
            {
                string wrapped = "{\"items\":" + responseText + "}";
                IssueListResponse response =
                    JsonUtility.FromJson<IssueListResponse>(wrapped);
                issues = response?.items;
            }
            catch (ArgumentException)
            {
                // ReadResponse below turns malformed success payloads into an error.
            }
        }
        completed?.Invoke(
            request.responseCode,
            issues == null && request.responseCode == 200
                ? "后端返回了无法解析的数据。"
                : ReadResponse(request),
            issues);
    }

    private static string ReadResponse(UnityWebRequest request)
    {
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            return "无法连接巡检后端，请确认 Go 服务已启动。";
        }
        if (request.result == UnityWebRequest.Result.DataProcessingError)
        {
            return "后端返回了无法解析的数据。";
        }

        string responseText = request.downloadHandler?.text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                ApiError error = JsonUtility.FromJson<ApiError>(responseText);
                if (error != null && !string.IsNullOrWhiteSpace(error.error))
                {
                    return error.error;
                }
            }
            catch (ArgumentException)
            {
                // A successful response is not an error envelope; status decides success.
            }
        }

        if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            return $"提交失败（HTTP {request.responseCode}）。";
        }
        return string.Empty;
    }
}

