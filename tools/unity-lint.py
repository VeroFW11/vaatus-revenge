#!/usr/bin/env python3
"""Project lint for Unity rules the C# compiler can't catch outside Unity.

Run by tools/compile-check.sh. Exit code 1 means at least one rule was broken.

Rules:
  1. Core purity: nothing under Scripts/Core may use UnityEngine/UnityEditor (the headless
     test harness compiles Core without Unity).
  2. A MonoBehaviour / ScriptableObject / Editor class must live in a file with the same name,
     or Unity can't attach it to objects or assets.
  3. Banned APIs: things that throw with this project's settings, or are deprecated in Unity 6.
  4. C# features newer than Unity's C# 9 subset.
  5. Every script in Scripts/, Editor/ and Tests/ is inside a VaatusRevenge namespace.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "game" / "Assets" / "_Project"

BANNED = [
    (r"\bFindObjectsOfType\b|\bFindObjectOfType\b", "deprecated in Unity 6: use a registry or a serialized reference"),
    (r"\bFindObjectsByType\b|\bFindFirstObjectByType\b|\bFindAnyObjectByType\b|\bFindObjectsSortMode\b",
     "avoid Find* scene searches: use a serialized reference, a static Instance or a registry"),
    (r"\bGetInstanceID\s*\(", "instance IDs are being replaced in Unity 6.x: use our own int ids"),
    (r"(?<![\w.])Input\.(GetAxis|GetAxisRaw|GetKey|GetKeyDown|GetKeyUp|GetButton|GetButtonDown|GetButtonUp|GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|mousePosition|mouseScrollDelta|anyKey|anyKeyDown)\b",
     "legacy Input Manager throws in this project (Active Input Handling = Input System): use UnityEngine.InputSystem"),
    (r"\bUnityEngine\.Input\.", "legacy Input Manager throws in this project: use UnityEngine.InputSystem"),
    (r"\bRigidbody\b", "no Rigidbody in the prototype: CharacterController + manual projectile sweeps (Unity 6 renamed velocity APIs)"),
    (r"\bSendMessage\s*\(|\bBroadcastMessage\s*\(", "use direct references or C# events"),
    (r"\basync\s+void\b", "no async void in gameplay code"),
    (r"#nullable\b", "nullable reference types are off in this project"),
    (r"\bGUIText\b|\bGUITexture\b|\bTextMesh\b", "legacy text components: use IMGUI (OnGUI) for grey-box text"),
    (r"\bPhysicMaterial\b", "renamed in Unity 6"),
    (r"\bPlayerInput\b(?!Frame|Reader)", "use the code-defined actions in PlayerInputReader, not the PlayerInput component"),
    (r"\bAssert\.Multiple\b", "not in Unity's NUnit build"),
    (r"\bClassicAssert\b", "NUnit 4 only; Unity uses NUnit 3"),
    (r"\[UnityTest\]", "EditMode core tests must be plain [Test] so they also run without Unity"),
]

NEW_CSHARP = [
    (r"^\s*namespace\s+[\w.]+\s*;", "file-scoped namespace is C# 10 (Unity uses C# 9): use a block namespace"),
    (r"^\s*global\s+using\b", "global using is C# 10"),
    (r"\brecord\s+(struct|class)\b|\bpublic\s+record\b|\binternal\s+record\b", "records need IsExternalInit: use a class or struct"),
    (r"\{\s*get;\s*init;\s*\}|\binit;", "init accessors need IsExternalInit: use a normal setter or constructor"),
    (r"\brequired\s+(public|private|internal|protected|\w+\s+\w+\s*[;{=])", "required members are C# 11"),
    (r'"""', "raw string literals are C# 11"),
]

UNITY_BASE = r"(MonoBehaviour|ScriptableObject|EditorWindow|Editor|PropertyDrawer|StateMachineBehaviour)"


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r'@"(?:[^"]|"")*"', '""', text)
    text = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', text)
    return text


def main():
    problems = []
    for path in sorted(ROOT.rglob("*.cs")):
        rel = path.relative_to(ROOT.parent.parent)
        raw = path.read_text(encoding="utf-8-sig")
        code = strip_comments(raw)
        in_core = "Scripts/Core/" in path.as_posix()

        if in_core and re.search(r"\busing\s+Unity(Engine|Editor)\b|\bUnity(Engine|Editor)\.", code):
            problems.append(f"{rel}: Core must not use UnityEngine/UnityEditor")

        for m in re.finditer(r"\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([\w.]+)", code):
            name, base = m.group(1), m.group(2).split(".")[-1]
            if re.fullmatch(UNITY_BASE, base) and path.stem != name:
                problems.append(f"{rel}: {base} '{name}' must be in a file named {name}.cs")

        for pattern, why in BANNED:
            for m in re.finditer(pattern, code):
                line = code[: m.start()].count("\n") + 1
                problems.append(f"{rel}:{line}: '{m.group(0).strip()}' - {why}")

        for pattern, why in NEW_CSHARP:
            for m in re.finditer(pattern, code, flags=re.M):
                line = code[: m.start()].count("\n") + 1
                problems.append(f"{rel}:{line}: {why}")

        if not re.search(r"^\s*namespace\s+VaatusRevenge\b", code, flags=re.M):
            problems.append(f"{rel}: put the code inside a VaatusRevenge.* namespace")

    for p in problems:
        print("  " + p)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
