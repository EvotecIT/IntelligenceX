// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "IntelligenceXSwift",
    platforms: [
        .iOS(.v17),
        .macOS(.v14),
        .watchOS(.v10),
    ],
    products: [
        .library(name: "IntelligenceXCodex", targets: ["IntelligenceXCodex"]),
        .library(name: "IntelligenceXCodexApple", targets: ["IntelligenceXCodexApple"]),
        .library(name: "IntelligenceXRealtime", targets: ["IntelligenceXRealtime"]),
        .library(name: "IntelligenceXRealtimeWebSocket", targets: ["IntelligenceXRealtimeWebSocket"]),
        .library(name: "IntelligenceXRealtimeWebRTC", targets: ["IntelligenceXRealtimeWebRTC"]),
    ],
    dependencies: [
        .package(
            url: "https://github.com/livekit/webrtc-xcframework.git",
            exact: "144.7559.11"
        ),
    ],
    targets: [
        .target(
            name: "IntelligenceXCodex",
            path: "Swift/Sources/IntelligenceXCodex"
        ),
        .target(
            name: "IntelligenceXCodexApple",
            dependencies: ["IntelligenceXCodex"],
            path: "Swift/Sources/IntelligenceXCodexApple",
            linkerSettings: [
                .linkedFramework("AuthenticationServices"),
                .linkedFramework("Network"),
                .linkedFramework("Security"),
            ]
        ),
        .target(
            name: "IntelligenceXRealtime",
            dependencies: ["IntelligenceXCodex"],
            path: "Swift/Sources/IntelligenceXRealtime"
        ),
        .target(
            name: "IntelligenceXRealtimeWebSocket",
            dependencies: ["IntelligenceXCodex", "IntelligenceXRealtime"],
            path: "Swift/Sources/IntelligenceXRealtimeWebSocket"
        ),
        .target(
            name: "IntelligenceXRealtimeWebRTC",
            dependencies: [
                "IntelligenceXCodex",
                "IntelligenceXRealtime",
                .product(name: "LiveKitWebRTC", package: "webrtc-xcframework"),
            ],
            path: "Swift/Sources/IntelligenceXRealtimeWebRTC"
        ),
        .testTarget(
            name: "IntelligenceXCodexTests",
            dependencies: ["IntelligenceXCodex"],
            path: "Swift/Tests/IntelligenceXCodexTests"
        ),
        .testTarget(
            name: "IntelligenceXCodexAppleTests",
            dependencies: ["IntelligenceXCodexApple"],
            path: "Swift/Tests/IntelligenceXCodexAppleTests"
        ),
        .testTarget(
            name: "IntelligenceXRealtimeTests",
            dependencies: ["IntelligenceXCodex", "IntelligenceXRealtime"],
            path: "Swift/Tests/IntelligenceXRealtimeTests"
        ),
        .testTarget(
            name: "IntelligenceXRealtimeWebRTCTests",
            dependencies: [
                "IntelligenceXRealtime",
                "IntelligenceXRealtimeWebRTC",
            ],
            path: "Swift/Tests/IntelligenceXRealtimeWebRTCTests"
        ),
        .testTarget(
            name: "IntelligenceXRealtimeWebSocketTests",
            dependencies: [
                "IntelligenceXCodex",
                "IntelligenceXRealtime",
                "IntelligenceXRealtimeWebSocket",
            ],
            path: "Swift/Tests/IntelligenceXRealtimeWebSocketTests"
        ),
    ]
)
