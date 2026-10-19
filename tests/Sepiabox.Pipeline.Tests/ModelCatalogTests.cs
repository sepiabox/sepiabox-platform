using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Tests;

public sealed class ModelCatalogTests
{
    private const string Revision = "d15189d7028b43f1d3e65039190477f6af591c2a";

    [Fact]
    public void Parses_a_valid_entry()
    {
        var model = Assert.Single(ModelCatalog.Parse(Json()));

        Assert.Equal(("clip", Revision), (model.Name, model.Revision));
    }

    [Theory]
    [InlineData("name", "Clip Model", "a name of lowercase letters")]
    [InlineData("file", "../clip.onnx", "a plain file name")]
    [InlineData("sha256", "ABC", "a SHA-256 of 64 lowercase hex characters")]
    [InlineData("revision", "main", "a revision that is a 40-character commit hash")]
    [InlineData("url", "https://huggingface.co/x/y/resolve/main/model.onnx", "an https URL that contains its revision")]
    [InlineData("url", "http://huggingface.co/x/y/resolve/d15189d7028b43f1d3e65039190477f6af591c2a/model.onnx", "an https URL")]
    [InlineData("license", "CC-BY-NC-4.0", "an AGPL-compatible license")]
    public void Refuses_an_entry_that_cannot_be_trusted(string property, string value, string expected)
    {
        var failure = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(Json(entry => entry[property] = value)));

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_entry_without_a_revision()
    {
        Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(Json(entry => entry.Remove("revision"))));
    }

    [Fact]
    public void Refuses_a_model_listed_twice()
    {
        var json = JsonNode.Parse(Json())!;
        json["models"]!.AsArray().Add(json["models"]![0]!.DeepClone());

        var failure = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(json.ToJsonString()));

        Assert.Contains("twice", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_embedded_list_is_valid()
    {
        Assert.NotEmpty(ModelCatalog.All);
    }

    private static string Json(Action<JsonObject>? change = null)
    {
        var entry = new JsonObject
        {
            ["name"] = "clip",
            ["file"] = "clip.onnx",
            ["url"] = $"https://huggingface.co/x/y/resolve/{Revision}/model.onnx",
            ["revision"] = Revision,
            ["sha256"] = new string('a', 64),
            ["license"] = "MIT",
            ["source"] = "tests",
        };
        change?.Invoke(entry);
        return new JsonObject { ["models"] = new JsonArray(entry) }.ToJsonString();
    }
}
