namespace MorePlayers
{
    public struct MultiStartRequestPacket
    {
        public ushort seqNum;

        public uint seed;

        public byte nrOfPlayers;

        public byte nrOfAbilites;

        public byte currentLevel;

        public byte frameBufferSize;

        public byte isDemoMask;

        public ulong[] p_ids;
        public byte[] p_colors;
        public byte[] p_teams;
        public byte[] p_ability1s;
        public byte[] p_ability2s;
        public byte[] p_ability3s;

        public void Initialize(int count)
        {
            p_ids = new ulong[count];
            p_colors = new byte[count];
            p_teams = new byte[count];
            p_ability1s = new byte[count];
            p_ability2s = new byte[count];
            p_ability3s = new byte[count];
        }

        public override string ToString()
        {
            return $"seqNum: {seqNum}, seed: {seed}, nrOfPlayers: {nrOfPlayers}, nrOfAbilites: {nrOfAbilites}, currentLevel: {currentLevel}, frameBufferSize: {frameBufferSize}, isDemoMask: {isDemoMask}, p_ids: {string.Join(", ", p_ids)}, p_colors: {string.Join(", ", p_colors)}, p_teams: {string.Join(", ", p_teams)}, p_ability1s: {string.Join(", ", p_ability1s)}, p_ability2s: {string.Join(", ", p_ability2s)}, p_ability3s: {string.Join(", ", p_ability3s)}";
        }
    }
}