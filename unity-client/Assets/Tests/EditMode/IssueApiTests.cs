using System;
using NUnit.Framework;
using UnityEngine;

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
}

