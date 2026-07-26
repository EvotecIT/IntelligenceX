import AuthenticationServices
import Foundation
import IntelligenceXCodex

#if canImport(UIKit)
import UIKit
#elseif canImport(AppKit)
import AppKit
#endif

/// Runs Codex's recommended ChatGPT browser OAuth flow without exposing credentials to the app.
@MainActor
public final class IXAppleCodexBrowserAuthenticator:
    NSObject,
    ASWebAuthenticationPresentationContextProviding
{
    private var webSession: ASWebAuthenticationSession?
    private var loopbackServer: IXCodexLoopbackAuthorizationServer?
    private var continuation: CheckedContinuation<IXCodexAuthBundle, Error>?
    private var authorizationTask: Task<Void, Never>?

    public override init() {
        super.init()
    }

    public func signIn(
        authSession: IXCodexAuthSession,
        prefersEphemeralWebBrowserSession: Bool = false
    ) async throws -> IXCodexAuthBundle {
        guard continuation == nil else {
            throw IXCodexError.invalidResponse("a ChatGPT browser sign-in is already active")
        }
        let callbackPorts = await authSession.browserCallbackPorts
        let server = try await IXCodexLoopbackAuthorizationServer.start(ports: callbackPorts)
        let redirectURL = URL(
            string: "http://localhost:\(server.port)/auth/callback"
        )!
        let authorization = try await authSession.beginBrowserAuthorization(
            redirectURL: redirectURL
        )
        loopbackServer = server

        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                self.continuation = continuation
                server.receiveCallback { [weak self] result in
                    Task { @MainActor in
                        guard let self else { return }
                        switch result {
                        case .success(let callbackURL):
                            self.complete(
                                authSession: authSession,
                                authorization: authorization,
                                callbackURL: callbackURL
                            )
                        case .failure(let error):
                            self.finish(.failure(error))
                        }
                    }
                }

                let webSession = ASWebAuthenticationSession(
                    url: authorization.authorizationURL,
                    callbackURLScheme: nil
                ) { [weak self] callbackURL, error in
                    Task { @MainActor in
                        guard let self, self.continuation != nil else { return }
                        if let callbackURL {
                            self.complete(
                                authSession: authSession,
                                authorization: authorization,
                                callbackURL: callbackURL
                            )
                        } else {
                            if let authenticationError = error as? ASWebAuthenticationSessionError,
                               authenticationError.code == .canceledLogin {
                                self.finish(.failure(CancellationError()))
                            } else {
                                self.finish(.failure(error ?? CancellationError()))
                            }
                        }
                    }
                }
                webSession.presentationContextProvider = self
                webSession.prefersEphemeralWebBrowserSession =
                    prefersEphemeralWebBrowserSession
                self.webSession = webSession
                guard webSession.start() else {
                    self.finish(.failure(
                        IXCodexError.invalidResponse("the secure browser session could not start")
                    ))
                    return
                }
            }
        } onCancel: {
            Task { @MainActor [weak self] in
                self?.finish(.failure(CancellationError()))
            }
        }
    }

    public func presentationAnchor(
        for session: ASWebAuthenticationSession
    ) -> ASPresentationAnchor {
        #if canImport(UIKit)
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        if let window = scenes.flatMap(\.windows).first(where: \.isKeyWindow) {
            return window
        }
        if let scene = scenes.first, let window = scene.windows.first {
            return window
        }
        return UIWindow()
        #elseif canImport(AppKit)
        return NSApplication.shared.keyWindow ?? NSWindow()
        #endif
    }

    private func complete(
        authSession: IXCodexAuthSession,
        authorization: IXCodexBrowserAuthorization,
        callbackURL: URL
    ) {
        guard continuation != nil, authorizationTask == nil else { return }
        authorizationTask = Task { [weak self] in
            guard let self else { return }
            do {
                let bundle = try await authSession.completeBrowserAuthorization(
                    authorization,
                    callbackURL: callbackURL
                )
                self.finish(.success(bundle))
            } catch {
                self.finish(.failure(error))
            }
        }
    }

    private func finish(_ result: Result<IXCodexAuthBundle, Error>) {
        guard let continuation else { return }
        self.continuation = nil
        authorizationTask?.cancel()
        authorizationTask = nil
        loopbackServer?.stop()
        loopbackServer = nil
        webSession?.cancel()
        webSession = nil
        continuation.resume(with: result)
    }
}
