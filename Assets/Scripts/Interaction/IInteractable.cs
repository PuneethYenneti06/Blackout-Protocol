using UnityEngine;

// Anything the player can use with E: doors (press), bomb and hostages (hold), power switch.
// Put the component on an object with a collider (or a parent of one).
public interface IInteractable
{
    // False hides the prompt (e.g. bomb already defused).
    bool CanInteract(Interactor who);

    // Verb phrase for the prompt, e.g. "open door", "defuse the bomb".
    string Prompt { get; }

    // 0 = press once. Above 0 = hold E this many seconds.
    float HoldTime { get; }

    // Called once when pressed (HoldTime 0) or when the hold completes.
    void Interact(Interactor who);

    // Called every frame while E is held on it (progress 0..1).
    void HoldTick(Interactor who, float progress);

    // Called when a hold stops before completing (released, moved away, got hit).
    void HoldCancelled(Interactor who);
}
