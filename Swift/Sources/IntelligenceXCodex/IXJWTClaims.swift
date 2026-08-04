import Foundation

enum IXJWTClaims {
    static func payload(token: String?) -> [String: IXJSONValue]? {
        guard let token else { return nil }
        let components = token.split(separator: ".", omittingEmptySubsequences: false)
        guard components.count >= 2,
              let data = Data(base64URLEncoded: String(components[1])),
              case .object(let payload) = try? IXJSONValue.decode(data) else {
            return nil
        }
        return payload
    }

    static func accountID(accessToken: String, idToken: String? = nil) -> String? {
        for token in [accessToken, idToken].compactMap({ $0 }) {
            guard let claims = payload(token: token) else { continue }
            if let direct = claims["chatgpt_account_id"]?.stringValue {
                return direct
            }
            if let auth = claims["https://api.openai.com/auth"]?.objectValue,
               let id = auth["chatgpt_account_id"]?.stringValue {
                return id
            }
        }
        return nil
    }

    static func account(bundle: IXCodexAuthBundle) -> IXCodexAccount? {
        let claims = payload(token: bundle.idToken) ?? payload(token: bundle.accessToken)
        guard let id = bundle.accountID ?? accountID(accessToken: bundle.accessToken, idToken: bundle.idToken) else {
            return nil
        }
        let email = claims?["email"]?.stringValue
        let auth = claims?["https://api.openai.com/auth"]?.objectValue
        let plan = auth?["chatgpt_plan_type"]?.stringValue ?? claims?["chatgpt_plan_type"]?.stringValue
        return IXCodexAccount(id: id, email: email, plan: plan)
    }
}

private extension Data {
    init?(base64URLEncoded value: String) {
        var base64 = value.replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        let remainder = base64.count % 4
        if remainder != 0 {
            base64.append(String(repeating: "=", count: 4 - remainder))
        }
        self.init(base64Encoded: base64)
    }
}
