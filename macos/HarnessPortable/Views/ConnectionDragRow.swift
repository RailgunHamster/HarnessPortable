import SwiftUI
import UniformTypeIdentifiers

private let connectionType = UTType(exportedAs: "com.harness.portable.connection")

/// A local connection payload; dropping unrelated text/URLs never edits the library.
struct ConnectionDragRow<Content: View>: View {
    let key: String
    let enabled: Bool
    let move: (String, Bool) -> Void
    @ViewBuilder let content: () -> Content
    @State private var height: CGFloat = 50
    @State private var insertion: Bool?

    var body: some View {
        Group {
            if enabled {
                content()
                    .background(GeometryReader { geometry in
                        Color.clear.onAppear { height = geometry.size.height }
                            .onChange(of: geometry.size.height) { height = $0 }
                    })
                    .overlay(alignment: insertion == true ? .bottom : .top) {
                        if insertion != nil { Rectangle().fill(Color.accentColor).frame(height: 2).allowsHitTesting(false) }
                    }
                    .onDrag {
                        let provider = NSItemProvider()
                        provider.registerDataRepresentation(forTypeIdentifier: connectionType.identifier, visibility: .ownProcess) { completion in
                            completion(Data(key.utf8), nil); return nil
                        }
                        return provider
                    }
                    .onDrop(of: [connectionType], delegate: ConnectionDrop(height: height, insertion: $insertion, move: move))
            } else { content() }
        }
    }
}

private struct ConnectionDrop: DropDelegate {
    let height: CGFloat
    @Binding var insertion: Bool?
    let move: (String, Bool) -> Void

    func validateDrop(info: DropInfo) -> Bool { info.hasItemsConforming(to: [connectionType]) }
    func dropUpdated(info: DropInfo) -> DropProposal? {
        insertion = info.location.y >= height / 2
        return DropProposal(operation: .move)
    }
    func dropExited(info: DropInfo) { insertion = nil }
    func performDrop(info: DropInfo) -> Bool {
        let after = info.location.y >= height / 2
        insertion = nil
        guard let provider = info.itemProviders(for: [connectionType]).first else { return false }
        provider.loadDataRepresentation(forTypeIdentifier: connectionType.identifier) { data, _ in
            guard let data, let key = String(data: data, encoding: .utf8) else { return }
            DispatchQueue.main.async { move(key, after) }
        }
        return true
    }
}
