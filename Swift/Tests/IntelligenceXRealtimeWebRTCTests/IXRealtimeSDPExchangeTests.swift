import Foundation
import XCTest
@testable import IntelligenceXRealtimeWebRTC

final class IXRealtimeSDPExchangeTests: XCTestCase {
    func testSDPIsEncodedAsAFormFieldInsteadOfAFileUpload() throws {
        let body = IXRealtimeMultipartForm.sdpBody(
            "v=0\r\na=example",
            boundary: "test-boundary"
        )
        let value = try XCTUnwrap(String(data: body, encoding: .utf8))

        XCTAssertTrue(value.contains("Content-Disposition: form-data; name=\"sdp\"\r\n"))
        XCTAssertFalse(value.contains("filename="))
        XCTAssertTrue(value.contains("Content-Type: application/sdp\r\n\r\nv=0\r\na=example"))
        XCTAssertTrue(value.hasSuffix("\r\n--test-boundary--\r\n"))
    }
}
