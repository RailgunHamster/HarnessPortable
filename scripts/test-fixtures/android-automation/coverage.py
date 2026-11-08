"""Source contract gate; runtime smoke additionally rejects untagged native actions."""
import json, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parents[3]
UI = ROOT / 'app/src/main/java/com/harness/portable'
KINDS = {
    'Button', 'OutlinedButton', 'TextButton', 'IconButton', 'SmallFloatingActionButton',
    'OutlinedTextField', 'Switch', 'Checkbox', 'DropdownMenuItem', 'FilterChip', 'Tab',
    'ScrollableTabRow', 'LazyColumn', 'AutomationAlertDialog', 'AutomationDropdownMenu',
    'ExposedDropdownMenu', 'ExposedDropdownMenuBox', 'AndroidView',
}
STRINGS = r'"""[\s\S]*?"""|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/'

def masked(text):
    return re.sub(STRINGS, lambda m: ' ' * len(m.group()), text)

def arguments(text, opening):
    code = masked(text); stack = []; start = opening + 1; parts = []
    for index in range(opening, len(code)):
        ch = code[index]
        if ch in '([{': stack.append(ch)
        elif ch in ')]}':
            assert stack and '([{'.index(stack.pop()) == ')]}'.index(ch), 'Unbalanced Kotlin arguments'
            if not stack: return parts + [text[start:index]]
        elif ch == ',' and len(stack) == 1: parts.append(text[start:index]); start = index + 1
    raise AssertionError('Unterminated Kotlin call')

errors = []; controls = 0; ids = set()
for path in UI.rglob('*.kt'):
    if path.is_relative_to(UI/'automation'): continue
    text = path.read_text(encoding='utf-8-sig'); code = masked(text)
    def problem(at, reason): errors.append(f'{path.name}:{text.count(chr(10), 0, at)+1}: {reason}')
    for call in re.finditer(r'\b([A-Za-z_]\w*)\s*\(', code):
        kind = call.group(1)
        if kind in ('AlertDialog', 'DropdownMenu'):
            # The one legacy menu also declares its native dismissal callback.
            args = arguments(text, code.index('(', call.start()))
            modifier = next((p for p in args if re.match(r'\s*modifier\s*=', p)), '')
            if kind == 'AlertDialog' or '.automationDismiss' not in modifier:
                problem(call.start(), 'Use an automation dialog/menu wrapper with the same dismiss callback')
        if kind in {'TextField', 'BasicTextField', 'RadioButton', 'Slider', 'RangeSlider', 'TriStateCheckbox',
                    'InputChip', 'SuggestionChip', 'ElevatedButton', 'OutlinedIconButton', 'FilledIconButton',
                    'FilledTonalButton', 'ExtendedFloatingActionButton', 'FloatingActionButton', 'NavigationBarItem',
                    'NavigationRailItem', 'NavigationDrawerItem', 'SearchBar', 'DockedSearchBar', 'DatePicker',
                    'TimePicker', 'PullToRefreshBox', 'SwipeToDismissBox', 'HorizontalPager', 'VerticalPager',
                    'LazyRow', 'LazyVerticalGrid', 'LazyHorizontalGrid', 'ModalBottomSheet', 'Dialog', 'SelectionContainer'}:
            problem(call.start(), f'New interactive kind {kind} needs an adapter behavior test and coverage registration')
        if kind not in KINDS and kind not in ('DropdownMenu',): continue
        if re.search(r'\bfun\s*$', code[max(0, call.start()-8):call.start()]): continue
        args = arguments(text, code.index('(', call.start()))
        modifier = next((p for p in args if re.match(r'\s*modifier\s*=', p)), '')
        if not modifier and kind == 'LazyColumn' and args: modifier = args[0]
        if not re.search(r'\.automationId\s*\(', modifier):
            problem(call.start(), f'{kind} requires a stable ID on its own modifier')
        controls += 1
    # Custom modifiers and scroll hosts must declare identity on their receiver chain.
    for match in re.finditer(r'\.(clickable|combinedClickable|verticalScroll|horizontalScroll)\s*\(', code):
        prefix = text[max(0, match.start()-300):match.start()]
        if '.automationId(' not in prefix: problem(match.start(), 'Gesture/scroll host requires a stable ID')
    for match in re.finditer(r'\b(pointerInput|detectTapGestures|detectDragGestures|onKeyEvent|onPreviewKeyEvent)\s*\(', code):
        problem(match.start(), 'New custom input needs a semantic operation, gesture mapping, and behavior test')
    for match in re.finditer(r'import androidx\.compose\.(?:material3|foundation)\.([A-Za-z_]\w*)', text):
        kind = match.group(1)
        if re.search(r'(?:Button|TextField|Checkbox|Switch|Chip|Slider|RadioButton|Tab|LazyRow|LazyColumn)$', kind) and kind not in KINDS:
            problem(match.start(), f'New interactive kind {kind} needs an adapter behavior test and coverage registration')
    ids.update(re.findall(r'\.automationId\("([A-Za-z][A-Za-z0-9_.]+)"', text))

runtime = '\n'.join(p.read_text(encoding='utf-8') for p in (UI/'automation').glob('*.kt'))
for forbidden in ('java.lang.reflect', 'getDeclaredMethod', 'getDeclaredField', 'dispatchTouchEvent', 'sendPointerSync', 'performClick('):
    if forbidden in runtime: errors.append('Native adapter cannot invoke private handlers or simulate screen input: '+forbidden)
manifest = (ROOT/'app/src/main/AndroidManifest.xml').read_text(encoding='utf-8')
assert re.search(r'<receiver\b[^>]*android:name="\.automation.AutomationReceiver"[^>]*android:permission="android.permission.DUMP"', manifest, re.S), 'ADB receiver must require DUMP permission'
gestures = json.loads((ROOT/'spec/android-ui-api-gestures.json').read_text(encoding='utf-8'))
all_source = runtime + '\n' + '\n'.join(p.read_text(encoding='utf-8-sig') for p in UI.glob('*.kt'))
for gesture, operations in gestures.items():
    for operation in operations:
        method, _, action = operation.partition(':')
        if f'"{method}"' not in all_source or action and f'"{action}"' not in runtime: errors.append(f'{gesture}: unregistered operation {operation}')
assert controls >= 120, 'UI source coverage unexpectedly shrank'
assert not errors, '\n'.join(errors)
print(json.dumps(dict(passed=True, interactiveCalls=controls, literalStableIds=len(ids), gestures=len(gestures))))
