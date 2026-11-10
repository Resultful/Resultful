using AwesomeAssertions;
using Matchmark.Cli;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary><c>matchmark diff</c>: each kind of change to a spec, and whom it breaks. A request may only loosen and a response only tighten.</summary>
[TestFixture]
public sealed class DiffTests
{
    private const string Base = """
        openapi: 3.1.0
        info: { title: Notes, version: 1.2.0 }
        paths:
          /notes:
            get:
              operationId: listNotes
              parameters: [{ name: tag, in: query, schema: { type: string } }]
              responses:
                '200': { description: notes, content: { application/json: { schema: { type: array, items: { $ref: '#/components/schemas/Note' } } } } }
            post:
              operationId: addNote
              requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/NewNote' } } } }
              responses:
                '201': { description: added, content: { application/json: { schema: { $ref: '#/components/schemas/Note' } } } }
          /notes/{noteId}:
            delete:
              operationId: deleteNote
              parameters: [{ name: noteId, in: path, required: true, schema: { type: integer } }]
              responses:
                '204': { description: deleted }
        components:
          schemas:
            NewNote:
              type: object
              required: [text]
              properties:
                text: { type: string, maxLength: 100 }
                colour: { enum: [red, blue], type: string }
            Note:
              type: object
              required: [id, text]
              properties:
                id: { type: integer }
                text: { type: string }
                colour: { type: string, enum: [red, blue] }
        """;

    private static IReadOnlyList<SpecChange> Diff(string from, string to, string? with = null)
        => SpecDiff.Compare(SpecJson.Normalize(Base, "notes.yaml"), SpecJson.Normalize(with ?? Base.Replace(from, to, StringComparison.Ordinal), "notes.yaml"));

    [Test]
    public void The_same_spec_has_no_changes() => Diff("", "", Base).Should().BeEmpty();

    [TestCase("text: { type: string, maxLength: 100 }", "text: { type: string, maxLength: 50 }", ChangeKind.Breaking, "addNote: request body.text's maxLength went from 100 to 50")]
    [TestCase("text: { type: string, maxLength: 100 }", "text: { type: string, maxLength: 200 }", ChangeKind.Additive, "addNote: request body.text's maxLength went from 100 to 200")]
    [TestCase("required: [text]", "required: [text, colour]", ChangeKind.Breaking, "addNote: request body.colour is now required")]
    [TestCase("colour: { enum: [red, blue], type: string }", "colour: { enum: [red, blue, green], type: string }", ChangeKind.Additive, "addNote: request body.colour can now be \"green\"")]
    [TestCase("colour: { enum: [red, blue], type: string }", "colour: { enum: [red], type: string }", ChangeKind.Breaking, "addNote: request body.colour can no longer be \"blue\"")]
    [TestCase("colour: { type: string, enum: [red, blue] }", "colour: { type: string, enum: [red, blue, green] }", ChangeKind.Breaking, "addNote: 201 response.colour can now be \"green\"")]
    [TestCase("required: [id, text]", "required: [id]", ChangeKind.Breaking, "addNote: 201 response.text is now optional")]
    [TestCase("id: { type: integer }", "id: { type: [integer, 'null'] }", ChangeKind.Breaking, "addNote: 201 response.id is now integer or null, was integer")]
    [TestCase("parameters: [{ name: tag, in: query, schema: { type: string } }]", "parameters: [{ name: tag, in: query, schema: { type: string } }, { name: page, in: query, required: true, schema: { type: integer } }]", ChangeKind.Breaking, "listNotes: the required parameter query.page was added")]
    [TestCase("parameters: [{ name: tag, in: query, schema: { type: string } }]", "parameters: [{ name: tag, in: query, schema: { type: string } }, { name: page, in: query, schema: { type: integer } }]", ChangeKind.Additive, "listNotes: the optional parameter query.page was added")]
    [TestCase("'204': { description: deleted }", "'204': { description: deleted }\n        '409': { description: in use }", ChangeKind.Breaking, "deleteNote: the 409 response was added, which a client of the old spec doesn't expect")]
    [TestCase("operationId: deleteNote", "operationId: removeNote", ChangeKind.Generated, "deleteNote: the operationId changed from deleteNote to removeNote")]
    public void Each_change_is_classified_by_whom_it_breaks(string from, string to, ChangeKind kind, string change)
        => Diff(from, to).Select(c => (c.Kind, $"{c.Subject}: {c.Message}")).Should().Contain((kind, change));

    [Test]
    public void Removing_an_operation_breaks_and_adding_one_does_not()
    {
        var removed = Diff("delete:", "patch:");

        removed.Should().ContainEquivalentOf(new SpecChange(ChangeKind.Breaking, "DELETE /notes/{}", "the operation was removed") { OperationId = "deleteNote" })
            .And.ContainEquivalentOf(new SpecChange(ChangeKind.Additive, "deleteNote", "the operation was added") { OperationId = "deleteNote" });
    }

    /// <summary>
    /// Step 5 of the flip: breaking changes judged against what consumers were seen doing. A change to an operation no
    /// recorded call reached, or to a response status nobody received, is listed but doesn't fail the run.
    /// </summary>
    [Test]
    public void A_breaking_change_no_consumer_was_seen_using_does_not_fail_the_run()
    {
        var changes = Diff("'204': { description: deleted }", "'204': { description: deleted }\n        '409': { description: in use }")
            .Concat(Diff("required: [id, text]", "required: [id]")).ToList();
        SpecDiff.Verdict(changes, "1.0.0", "1.0.0", allowBreaking: false).Failed.Should().BeTrue("without observed calls, every breaking change counts");

        // Consumers listed notes and added them, but never deleted one: the new 409 on deleteNote reaches nobody.
        var observed = SpecDiff.Observe(changes, SpecDiff.Calls("""[{"operationId":"addNote","status":201},{"operationId":"listNotes","status":200}]"""));
        observed.Should().Contain(c => c.Subject == "deleteNote" && c.Observed == false);
        observed.Should().Contain(c => c.Subject == "addNote" && c.Message.StartsWith("201 response.text", StringComparison.Ordinal) && c.Observed == true);
        SpecDiff.Verdict(observed, "1.0.0", "1.0.0", allowBreaking: false).Failed.Should().BeTrue("addNote's change reaches a consumer");

        var onlyDelete = SpecDiff.Observe(changes.Where(c => c.Subject == "deleteNote").ToList(), SpecDiff.Calls("""[{"operationId":"addNote","status":201}]"""));
        var verdict = SpecDiff.Verdict(onlyDelete, "1.0.0", "1.0.0", allowBreaking: false);
        verdict.Failed.Should().BeFalse();
        verdict.Verdict.Should().Contain("1 breaking but unobserved");
    }

    [Test]
    public void Renaming_a_path_parameter_is_no_change()
        => Diff("{noteId}", "{id}").Concat(Diff("name: noteId", "name: id")).Where(c => c.Kind != ChangeKind.Additive && !c.Message.Contains("parameter", StringComparison.Ordinal))
            .Should().BeEmpty();

    [TestCase("1.2.0", "1.3.0", false, true)]
    [TestCase("1.2.0", "2.0.0", false, false)]
    [TestCase("1.2.0", "1.3.0", true, false)]
    public void A_breaking_change_fails_unless_the_major_version_goes_up(string before, string after, bool allow, bool failed)
        => SpecDiff.Verdict([new SpecChange(ChangeKind.Breaking, "x", "y")], before, after, allow).Failed.Should().Be(failed);

    [Test]
    public void Additive_changes_pass_whatever_the_version()
        => SpecDiff.Verdict([new SpecChange(ChangeKind.Additive, "x", "y")], "1.0.0", "1.0.0", false).Failed.Should().BeFalse();
}
