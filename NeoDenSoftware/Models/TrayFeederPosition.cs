namespace NeoDenSoftware.Models;

/// <summary>
/// A tray feeder slot: unlike a tape feeder (one fixed X/Y), a tray holds parts in a grid running
/// from (BeginX, BeginY) to (EndX, EndY), Rows x Columns of them. World-space mm, same coordinate
/// frame as the other fixtures - not board-relative.
/// </summary>
public sealed record TrayFeederPosition(int FeederId, double BeginX, double BeginY, double EndX, double EndY, int Rows, int Columns);
