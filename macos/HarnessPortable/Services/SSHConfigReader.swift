import Foundation

struct SSHConfigHost: Identifiable, Hashable {
    let alias: String
    let hostName: String
    let user: String?
    let port: Int

    var id: String { alias.lowercased() }

    var connectionLabel: String {
        "\(user?.isEmpty == false ? user! : "未设置用户")@\(hostName):\(port)"
    }
}

enum SSHConfigReader {
    private static let supportedOptions = ["hostname", "user", "port"]

    static var defaultURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".ssh", isDirectory: true)
            .appendingPathComponent("config", isDirectory: false)
    }

    static func loadDefault() -> [SSHConfigHost] {
        guard let text = try? String(contentsOf: defaultURL, encoding: .utf8) else {
            return []
        }
        return parse(text)
    }

    static func parse(_ text: String) -> [SSHConfigHost] {
        var sections: [Section] = []

        for rawLine in text.split(whereSeparator: \.isNewline) {
            let line = stripComment(String(rawLine)).trimmingCharacters(in: .whitespacesAndNewlines)
            guard !line.isEmpty else { continue }
            let (key, value) = splitDirective(line)
            guard !key.isEmpty else { continue }

            if key.caseInsensitiveCompare("host") == .orderedSame {
                let patterns = tokenize(value)
                if !patterns.isEmpty {
                    sections.append(Section(patterns: patterns))
                }
                continue
            }

            guard !sections.isEmpty,
                  supportedOptions.contains(where: { $0.caseInsensitiveCompare(key) == .orderedSame }) else {
                continue
            }
            let normalizedKey = key.lowercased()
            if sections[sections.count - 1].options[normalizedKey] == nil {
                sections[sections.count - 1].options[normalizedKey] =
                    value.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))
            }
        }

        var aliases: [String] = []
        var seen = Set<String>()
        for section in sections {
            for pattern in section.patterns where isLiteralAlias(pattern) {
                let key = pattern.lowercased()
                if seen.insert(key).inserted {
                    aliases.append(pattern)
                }
            }
        }

        return aliases.compactMap { alias in
            var options: [String: String] = [:]
            for section in sections where matches(section.patterns, alias) {
                for option in supportedOptions {
                    let key = option.lowercased()
                    if options[key] == nil, let value = section.options[key] {
                        options[key] = value
                    }
                }
            }

            let hostName = nonEmpty(options["hostname"]) ?? alias
            let user = nonEmpty(options["user"])
            let portValue = options["port"]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            let port: Int
            if portValue.isEmpty {
                port = 22
            } else {
                guard let parsedPort = Int(portValue), (1...65535).contains(parsedPort) else {
                    // Do not turn a malformed config into an accidental port 22 connection.
                    return nil
                }
                port = parsedPort
            }

            return SSHConfigHost(alias: alias, hostName: hostName, user: user, port: port)
        }
    }

    private static func nonEmpty(_ value: String?) -> String? {
        guard let value else { return nil }
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }

    private static func isLiteralAlias(_ pattern: String) -> Bool {
        !pattern.isEmpty && !pattern.hasPrefix("!") &&
            !pattern.contains("*") && !pattern.contains("?")
    }

    private static func matches(_ patterns: [String], _ alias: String) -> Bool {
        var hasPositive = false
        var positiveMatch = false
        for rawPattern in patterns {
            let pattern = rawPattern.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !pattern.isEmpty else { continue }
            if pattern.hasPrefix("!") {
                if globMatches(String(pattern.dropFirst()), alias) { return false }
            } else {
                hasPositive = true
                positiveMatch = positiveMatch || globMatches(pattern, alias)
            }
        }
        return !hasPositive || positiveMatch
    }

    private static func globMatches(_ pattern: String, _ value: String) -> Bool {
        var expression = "^"
        for character in pattern {
            switch character {
            case "*": expression += ".*"
            case "?": expression += "."
            default: expression += NSRegularExpression.escapedPattern(for: String(character))
            }
        }
        expression += "$"
        return value.range(of: expression, options: [.regularExpression, .caseInsensitive]) != nil
    }

    private static func splitDirective(_ line: String) -> (String, String) {
        let characters = Array(line)
        guard let index = characters.firstIndex(where: { $0 == " " || $0 == "\t" || $0 == "=" }) else {
            return (line, "")
        }
        let key = String(characters[..<index]).trimmingCharacters(in: .whitespacesAndNewlines)
        var valueStart = index
        while valueStart < characters.count &&
                (characters[valueStart] == " " || characters[valueStart] == "\t" || characters[valueStart] == "=") {
            valueStart += 1
        }
        return (key, String(characters[valueStart...]))
    }

    private static func tokenize(_ value: String) -> [String] {
        var tokens: [String] = []
        var token = ""
        var quote: Character?
        for character in value {
            if let activeQuote = quote {
                if character == activeQuote {
                    quote = nil
                } else {
                    token.append(character)
                }
            } else if character == "\"" || character == "'" {
                quote = character
            } else if character.isWhitespace {
                if !token.isEmpty {
                    tokens.append(token)
                    token = ""
                }
            } else {
                token.append(character)
            }
        }
        if !token.isEmpty { tokens.append(token) }
        return tokens
    }

    private static func stripComment(_ line: String) -> String {
        var result = ""
        var quote: Character?
        var previous: Character?
        for character in line {
            if let activeQuote = quote {
                result.append(character)
                if character == activeQuote { quote = nil }
            } else if character == "\"" || character == "'" {
                quote = character
                result.append(character)
            } else if character == "#" && (previous == nil || previous?.isWhitespace == true) {
                break
            } else {
                result.append(character)
            }
            previous = character
        }
        return result
    }

    private struct Section {
        let patterns: [String]
        var options: [String: String] = [:]
    }
}
