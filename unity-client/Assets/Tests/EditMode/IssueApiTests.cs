using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class IssueApiTests
{
    [Test]
    public void PositionIsRoundedToMillimetres()
    {
        IssuePosition position = IssuePosition.From(
            new Vector3(1.23456f, 0.80004f, -2.49999f));

        Assert.That(position.x, Is.EqualTo(1.235f).Within(0.00001f));
        Assert.That(position.y, Is.EqualTo(0.8f).Within(0.00001f));
        Assert.That(position.z, Is.EqualTo(-2.5f).Within(0.00001f));
    }

    [TestCase(0, "low")]
    [TestCase(1, "medium")]
    [TestCase(2, "high")]
    public void PayloadMapsPriorityAndTrimsText(int index, string expected)
    {
        IssueCreatePayload payload = IssueApi.BuildPayload(
            "  1 号机柜温度异常  ",
            "  右侧温度偏高  ",
            index,
            new Vector3(1.25f, 0.8f, 2.4f));

        Assert.That(payload.title, Is.EqualTo("1 号机柜温度异常"));
        Assert.That(payload.description, Is.EqualTo("右侧温度偏高"));
        Assert.That(payload.priority, Is.EqualTo(expected));
        Assert.That(payload.position.x, Is.EqualTo(1.25f));
        Assert.That(payload.position.y, Is.EqualTo(0.8f));
        Assert.That(payload.position.z, Is.EqualTo(2.4f));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void BlankTitleIsRejected(string title)
    {
        Assert.Throws<ArgumentException>(() =>
            IssueApi.BuildPayload(
                title,
                string.Empty,
                0,
                Vector3.zero));
    }

    [Test]
    public void InvalidPriorityIndexIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IssueApi.BuildPayload(
                "valid title",
                string.Empty,
                3,
                Vector3.zero));
    }

    [Test]
    public void SerializedPayloadUsesContractFieldNames()
    {
        IssueCreatePayload payload = IssueApi.BuildPayload(
            "管线渗漏",
            "接头位置有水迹",
            2,
            new Vector3(1f, 2f, 3f));

        string json = JsonUtility.ToJson(payload);

        StringAssert.Contains("\"title\":\"管线渗漏\"", json);
        StringAssert.Contains("\"description\":\"接头位置有水迹\"", json);
        StringAssert.Contains("\"priority\":\"high\"", json);
        StringAssert.Contains("\"position\":{\"x\":1.0,\"y\":2.0,\"z\":3.0}", json);
        Assert.That(json, Does.Not.Contain("id"));
        Assert.That(json, Does.Not.Contain("status"));
        Assert.That(json, Does.Not.Contain("createdAt"));
    }

    [Test]
    public void LookRotatesPlayerForYawAndCameraForPitch()
    {
        GameObject player = new GameObject("Test Player");
        GameObject cameraObject = new GameObject("Test Camera");
        GameObject appObject = new GameObject("Test App");
        try
        {
            cameraObject.transform.SetParent(player.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            InspectionApp app = appObject.AddComponent<InspectionApp>();

            SetField(app, "viewRoot", player.transform);
            SetField(app, "inspectionCamera", camera);

            Invoke(app, "ApplyLook", 15f, 10f);

            Assert.That(
                Quaternion.Angle(player.transform.rotation, Quaternion.Euler(0f, 15f, 0f)),
                Is.LessThan(0.1f));
            Assert.That(
                Quaternion.Angle(
                    cameraObject.transform.localRotation,
                    Quaternion.Euler(10f, 0f, 0f)),
                Is.LessThan(0.1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(appObject);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void PrioritySelectorBuildsLowMediumAndHigh()
    {
        GameObject root = new GameObject("Test Canvas", typeof(RectTransform));
        GameObject appObject = new GameObject("Test App");
        try
        {
            InspectionApp app = appObject.AddComponent<InspectionApp>();
            Invoke(app, "CreatePrioritySelector", root.transform, 0f);

            Button[] buttons = (Button[])GetField(app, "priorityButtons");
            Assert.That(buttons, Has.Length.EqualTo(3));
            Assert.That(buttons[0].GetComponentInChildren<Text>().text, Is.EqualTo("低"));
            Assert.That(buttons[1].GetComponentInChildren<Text>().text, Is.EqualTo("中"));
            Assert.That(buttons[2].GetComponentInChildren<Text>().text, Is.EqualTo("高"));

            Invoke(app, "SelectPriority", 2);
            Assert.That(GetField(app, "priorityIndex"), Is.EqualTo(2));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(appObject);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static object Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(
            method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, $"Missing method {method}");
        return info.Invoke(target, arguments);
    }

    private static object GetField(object target, string name)
    {
        FieldInfo info = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, $"Missing field {name}");
        return info.GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo info = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, $"Missing field {name}");
        info.SetValue(target, value);
    }
}

