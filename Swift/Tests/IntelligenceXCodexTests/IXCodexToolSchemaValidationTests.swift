import XCTest
@testable import IntelligenceXCodex

final class IXCodexToolSchemaValidationTests: XCTestCase {
    func testStrictSchemaAcceptsNullableRequiredProperty() throws {
        let tool = IXCodexToolDefinition(
            name: "lookup",
            description: "Looks up a value.",
            parameters: .object([
                "type": .string("object"),
                "properties": .object([
                    "query": .object([
                        "type": .array([.string("string"), .string("null")]),
                    ]),
                ]),
                "required": .array([.string("query")]),
                "additionalProperties": .bool(false),
            ]),
            strict: true
        )

        XCTAssertNoThrow(try IXCodexToolSchemaValidator.validate([tool]))
    }

    func testStrictSchemaRejectsOptionalPropertyOmittedFromRequired() {
        let tool = IXCodexToolDefinition(
            name: "lookup",
            description: "Looks up a value.",
            parameters: .object([
                "type": .string("object"),
                "properties": .object([
                    "query": .object(["type": .string("string")]),
                    "limit": .object(["type": .string("number")]),
                ]),
                "required": .array([.string("query")]),
                "additionalProperties": .bool(false),
            ]),
            strict: true
        )

        XCTAssertThrowsError(try IXCodexToolSchemaValidator.validate([tool])) { error in
            XCTAssertEqual(
                error as? IXCodexToolSchemaIssue,
                .init(
                    toolName: "lookup",
                    path: "$",
                    message: "missing required fields: limit"
                )
            )
        }
    }

    func testStrictSchemaRejectsNestedObjectThatAllowsUnknownFields() {
        let tool = IXCodexToolDefinition(
            name: "control",
            description: "Controls a device.",
            parameters: .object([
                "type": .string("object"),
                "properties": .object([
                    "target": .object([
                        "type": .string("object"),
                        "properties": .object([:]),
                        "required": .array([]),
                    ]),
                ]),
                "required": .array([.string("target")]),
                "additionalProperties": .bool(false),
            ]),
            strict: true
        )

        XCTAssertThrowsError(try IXCodexToolSchemaValidator.validate([tool])) { error in
            XCTAssertEqual((error as? IXCodexToolSchemaIssue)?.path, "$.properties.target")
        }
    }

    func testStrictSchemaRejectsPrimitiveFunctionParameters() {
        let tool = IXCodexToolDefinition(
            name: "lookup",
            description: "Looks up a value.",
            parameters: .object(["type": .string("string")]),
            strict: true
        )

        XCTAssertThrowsError(try IXCodexToolSchemaValidator.validate([tool])) { error in
            XCTAssertEqual(
                error as? IXCodexToolSchemaIssue,
                .init(
                    toolName: "lookup",
                    path: "$",
                    message: "Strict function parameters must have type object."
                )
            )
        }
    }

    func testStrictSchemaRejectsNonObjectPropertiesKeyword() {
        let tool = IXCodexToolDefinition(
            name: "lookup",
            description: "Looks up a value.",
            parameters: .object([
                "type": .string("object"),
                "properties": .array([]),
                "required": .array([]),
                "additionalProperties": .bool(false),
            ]),
            strict: true
        )

        XCTAssertThrowsError(try IXCodexToolSchemaValidator.validate([tool])) { error in
            XCTAssertEqual(
                error as? IXCodexToolSchemaIssue,
                .init(
                    toolName: "lookup",
                    path: "$",
                    message: "The properties keyword must be a JSON object."
                )
            )
        }
    }

    func testNonStrictSchemaRemainsBackwardCompatible() throws {
        let tool = IXCodexToolDefinition(
            name: "legacy",
            description: "A legacy tool.",
            parameters: .object(["type": .string("object")])
        )

        XCTAssertNoThrow(try IXCodexToolSchemaValidator.validate([tool]))
    }
}
