/* RAGEFORGE - RDNA2 Opcode Tables */

namespace RageForge.Core.Shaders;

internal static class Tables
{
    private static Dictionary<uint, string> Map(params (uint Op, string Name)[] items) =>
        items.ToDictionary(i => i.Op, i => i.Name);

    private static Dictionary<uint, string> Seq(uint start, params string[] names) =>
        names.Select((n, k) => (start + (uint)k, n)).Where(x => x.n.Length > 0).ToDictionary(x => x.Item1, x => x.n);

    public static readonly Dictionary<uint, string> Sop2 = Seq(0,
        "s_add_u32", "s_sub_u32", "s_add_i32", "s_sub_i32", "s_addc_u32", "s_subb_u32", "s_min_i32", "s_min_u32",
        "s_max_i32", "s_max_u32", "s_cselect_b32", "s_cselect_b64", "", "", "s_and_b32", "s_and_b64",
        "s_or_b32", "s_or_b64", "s_xor_b32", "s_xor_b64", "s_andn2_b32", "s_andn2_b64", "s_orn2_b32", "s_orn2_b64",
        "s_nand_b32", "s_nand_b64", "s_nor_b32", "s_nor_b64", "s_xnor_b32", "s_xnor_b64", "s_lshl_b32", "s_lshl_b64",
        "s_lshr_b32", "s_lshr_b64", "s_ashr_i32", "s_ashr_i64", "s_bfm_b32", "s_bfm_b64", "s_mul_i32", "s_bfe_u32",
        "s_bfe_i32", "s_bfe_u64", "s_bfe_i64", "", "s_absdiff_i32", "", "s_lshl1_add_u32", "s_lshl2_add_u32",
        "s_lshl3_add_u32", "s_lshl4_add_u32", "s_pack_ll_b32_b16", "s_pack_lh_b32_b16", "s_pack_hh_b32_b16", "s_mul_hi_u32",
        "s_mul_hi_i32");

    public static readonly Dictionary<uint, string> Sopk = Seq(0,
        "s_movk_i32", "s_version", "s_cmovk_i32", "s_cmpk_eq_i32", "s_cmpk_lg_i32", "s_cmpk_gt_i32", "s_cmpk_ge_i32",
        "s_cmpk_lt_i32", "s_cmpk_le_i32", "s_cmpk_eq_u32", "s_cmpk_lg_u32", "s_cmpk_gt_u32", "s_cmpk_ge_u32",
        "s_cmpk_lt_u32", "s_cmpk_le_u32", "s_addk_i32", "s_mulk_i32", "", "s_getreg_b32", "s_setreg_b32", "",
        "s_setreg_imm32_b32", "s_call_b64", "s_waitcnt_vscnt", "s_waitcnt_vmcnt", "s_waitcnt_expcnt",
        "s_waitcnt_lgkmcnt", "s_subvector_loop_begin", "s_subvector_loop_end");

    public static readonly Dictionary<uint, string> Sop1 = Map(
        (3, "s_mov_b32"), (4, "s_mov_b64"), (5, "s_cmov_b32"), (6, "s_cmov_b64"), (7, "s_not_b32"), (8, "s_not_b64"),
        (9, "s_wqm_b32"), (10, "s_wqm_b64"), (11, "s_brev_b32"), (12, "s_brev_b64"), (13, "s_bcnt0_i32_b32"),
        (14, "s_bcnt0_i32_b64"), (15, "s_bcnt1_i32_b32"), (16, "s_bcnt1_i32_b64"), (17, "s_ff0_i32_b32"),
        (18, "s_ff0_i32_b64"), (19, "s_ff1_i32_b32"), (20, "s_ff1_i32_b64"), (21, "s_flbit_i32_b32"),
        (22, "s_flbit_i32_b64"), (23, "s_flbit_i32"), (24, "s_flbit_i32_i64"),
        (25, "s_sext_i32_i8"), (26, "s_sext_i32_i16"), (27, "s_bitset0_b32"), (29, "s_bitset1_b32"),
        (31, "s_getpc_b64"), (32, "s_setpc_b64"), (33, "s_swappc_b64"), (34, "s_rfe_b64"),
        (36, "s_and_saveexec_b64"), (37, "s_or_saveexec_b64"), (38, "s_xor_saveexec_b64"),
        (39, "s_andn2_saveexec_b64"), (40, "s_orn2_saveexec_b64"), (41, "s_nand_saveexec_b64"),
        (42, "s_nor_saveexec_b64"), (43, "s_xnor_saveexec_b64"), (44, "s_quadmask_b32"), (45, "s_quadmask_b64"),
        (46, "s_movrels_b32"), (47, "s_movrels_b64"), (48, "s_movreld_b32"), (49, "s_movreld_b64"), (52, "s_abs_i32"),
        (55, "s_andn1_saveexec_b64"), (56, "s_orn1_saveexec_b64"), (57, "s_andn1_wrexec_b64"), (58, "s_andn2_wrexec_b64"),
        (59, "s_bitreplicate_b64_b32"), (60, "s_and_saveexec_b32"), (61, "s_or_saveexec_b32"), (62, "s_xor_saveexec_b32"),
        (63, "s_andn2_saveexec_b32"), (64, "s_orn2_saveexec_b32"), (65, "s_nand_saveexec_b32"), (66, "s_nor_saveexec_b32"),
        (67, "s_xnor_saveexec_b32"), (68, "s_andn1_saveexec_b32"), (69, "s_orn1_saveexec_b32"), (70, "s_andn1_wrexec_b32"),
        (71, "s_andn2_wrexec_b32"), (73, "s_movrelsd_2_b32"));

    public static readonly Dictionary<uint, string> Sopc = Map(
        (0, "s_cmp_eq_i32"), (1, "s_cmp_lg_i32"), (2, "s_cmp_gt_i32"), (3, "s_cmp_ge_i32"), (4, "s_cmp_lt_i32"),
        (5, "s_cmp_le_i32"), (6, "s_cmp_eq_u32"), (7, "s_cmp_lg_u32"), (8, "s_cmp_gt_u32"), (9, "s_cmp_ge_u32"),
        (10, "s_cmp_lt_u32"), (11, "s_cmp_le_u32"), (12, "s_bitcmp0_b32"), (13, "s_bitcmp1_b32"), (14, "s_bitcmp0_b64"),
        (15, "s_bitcmp1_b64"), (18, "s_cmp_eq_u64"), (19, "s_cmp_lg_u64"));

    public static readonly Dictionary<uint, string> Sopp = Map(
        (0, "s_nop"), (1, "s_endpgm"), (2, "s_branch"), (3, "s_wakeup"), (4, "s_cbranch_scc0"), (5, "s_cbranch_scc1"),
        (6, "s_cbranch_vccz"), (7, "s_cbranch_vccnz"), (8, "s_cbranch_execz"), (9, "s_cbranch_execnz"), (10, "s_barrier"),
        (11, "s_setkill"), (12, "s_waitcnt"), (13, "s_sethalt"), (14, "s_sleep"), (15, "s_setprio"), (16, "s_sendmsg"),
        (17, "s_sendmsghalt"), (18, "s_trap"), (19, "s_icache_inv"), (20, "s_incperflevel"), (21, "s_decperflevel"),
        (22, "s_ttracedata"), (27, "s_endpgm_saved"), (30, "s_endpgm_ordered_ps_done"), (31, "s_code_end"),
        (32, "s_inst_prefetch"), (33, "s_clause"), (35, "s_waitcnt_depctr"), (36, "s_round_mode"), (37, "s_denorm_mode"),
        (40, "s_ttracedata_imm"));

    public static readonly Dictionary<uint, string> Smem = Map(
        (0, "s_load_dword"), (1, "s_load_dwordx2"), (2, "s_load_dwordx4"), (3, "s_load_dwordx8"), (4, "s_load_dwordx16"),
        (8, "s_buffer_load_dword"), (9, "s_buffer_load_dwordx2"), (10, "s_buffer_load_dwordx4"),
        (11, "s_buffer_load_dwordx8"), (12, "s_buffer_load_dwordx16"), (31, "s_gl1_inv"), (32, "s_dcache_inv"),
        (36, "s_memtime"), (37, "s_memrealtime"), (38, "s_atc_probe"), (39, "s_atc_probe_buffer"));

    public static readonly Dictionary<uint, string> Vop2 = Map(
        (1, "v_cndmask_b32"), (2, "v_dot2c_f32_f16"), (3, "v_add_f32"), (4, "v_sub_f32"), (5, "v_subrev_f32"),
        (6, "v_fmac_legacy_f32"), (7, "v_mul_legacy_f32"), (8, "v_mul_f32"), (9, "v_mul_i32_i24"), (10, "v_mul_hi_i32_i24"),
        (11, "v_mul_u32_u24"), (12, "v_mul_hi_u32_u24"), (13, "v_dot4c_i32_i8"), (15, "v_min_f32"), (16, "v_max_f32"),
        (17, "v_min_i32"), (18, "v_max_i32"), (19, "v_min_u32"), (20, "v_max_u32"), (22, "v_lshrrev_b32"),
        (24, "v_ashrrev_i32"), (26, "v_lshlrev_b32"), (27, "v_and_b32"), (28, "v_or_b32"), (29, "v_xor_b32"),
        (30, "v_xnor_b32"), (31, "v_mac_f32"), (32, "v_madmk_f32"), (33, "v_madak_f32"),(37, "v_add_nc_u32"), (38, "v_sub_nc_u32"), (39, "v_subrev_nc_u32"), (40, "v_add_co_ci_u32"),
        (41, "v_sub_co_ci_u32"), (42, "v_subrev_co_ci_u32"), (43, "v_fmac_f32"), (44, "v_fmamk_f32"), (45, "v_fmaak_f32"),
        (47, "v_cvt_pkrtz_f16_f32"), (50, "v_add_f16"), (51, "v_sub_f16"), (52, "v_subrev_f16"), (53, "v_mul_f16"),
        (54, "v_fmac_f16"), (55, "v_fmamk_f16"), (56, "v_fmaak_f16"), (57, "v_max_f16"), (58, "v_min_f16"),
        (59, "v_ldexp_f16"), (60, "v_pk_fmac_f16"));

    public static readonly Dictionary<uint, string> Vop1 = Map(
        (0, "v_nop"), (1, "v_mov_b32"), (2, "v_readfirstlane_b32"), (3, "v_cvt_i32_f64"), (4, "v_cvt_f64_i32"),
        (5, "v_cvt_f32_i32"), (6, "v_cvt_f32_u32"), (7, "v_cvt_u32_f32"), (8, "v_cvt_i32_f32"), (10, "v_cvt_f16_f32"),
        (11, "v_cvt_f32_f16"), (12, "v_cvt_rpi_i32_f32"), (13, "v_cvt_flr_i32_f32"), (14, "v_cvt_off_f32_i4"),
        (15, "v_cvt_f32_f64"), (16, "v_cvt_f64_f32"), (17, "v_cvt_f32_ubyte0"), (18, "v_cvt_f32_ubyte1"),
        (19, "v_cvt_f32_ubyte2"), (20, "v_cvt_f32_ubyte3"), (21, "v_cvt_u32_f64"), (22, "v_cvt_f64_u32"),
        (23, "v_trunc_f64"), (24, "v_ceil_f64"), (25, "v_rndne_f64"), (26, "v_floor_f64"), (27, "v_pipeflush"),
        (32, "v_fract_f32"), (33, "v_trunc_f32"), (34, "v_ceil_f32"), (35, "v_rndne_f32"), (36, "v_floor_f32"),
        (37, "v_exp_f32"), (39, "v_log_f32"), (42, "v_rcp_f32"), (43, "v_rcp_iflag_f32"), (46, "v_rsq_f32"),
        (47, "v_rcp_f64"), (49, "v_rsq_f64"), (51, "v_sqrt_f32"), (52, "v_sqrt_f64"), (53, "v_sin_f32"), (54, "v_cos_f32"),
        (55, "v_not_b32"), (56, "v_bfrev_b32"), (57, "v_ffbh_u32"), (58, "v_ffbl_b32"), (59, "v_ffbh_i32"),
        (60, "v_frexp_exp_i32_f64"), (61, "v_frexp_mant_f64"), (62, "v_fract_f64"), (63, "v_frexp_exp_i32_f32"),
        (64, "v_frexp_mant_f32"), (65, "v_clrexcp"), (66, "v_movreld_b32"), (67, "v_movrels_b32"), (68, "v_movrelsd_b32"),
        (72, "v_movrelsd_2_b32"), (80, "v_cvt_f16_u16"), (81, "v_cvt_f16_i16"), (82, "v_cvt_u16_f16"), (83, "v_cvt_i16_f16"),
        (84, "v_rcp_f16"), (85, "v_sqrt_f16"), (86, "v_rsq_f16"), (87, "v_log_f16"), (88, "v_exp_f16"),
        (89, "v_frexp_mant_f16"), (90, "v_frexp_exp_i16_f16"), (91, "v_floor_f16"), (92, "v_ceil_f16"), (93, "v_trunc_f16"),
        (94, "v_rndne_f16"), (95, "v_fract_f16"), (96, "v_sin_f16"), (97, "v_cos_f16"), (98, "v_sat_pk_u8_i16"),
        (99, "v_cvt_norm_i16_f16"), (100, "v_cvt_norm_u16_f16"), (101, "v_swap_b32"), (104, "v_swaprel_b32"));

    private static readonly string[] FloatCmp = ["f", "lt", "eq", "le", "gt", "lg", "ge", "o", "u", "nge", "nlg", "ngt", "nle", "neq", "nlt", "tru"];
    private static readonly string[] IntCmp = ["f", "lt", "eq", "le", "gt", "ne", "ge", "t"];

    public static string VopcName(uint op)
    {
        uint hi = op >> 4, lo = op & 0xF;
        return hi switch
        {
            0x0 => $"v_cmp_{FloatCmp[lo]}_f32",
            0x1 => $"v_cmpx_{FloatCmp[lo]}_f32",
            0x2 => $"v_cmp_{FloatCmp[lo]}_f64",
            0x3 => $"v_cmpx_{FloatCmp[lo]}_f64",
            0x8 when lo < 8 => $"v_cmp_{IntCmp[lo]}_i32",
            0x8 when lo == 8 => "v_cmp_class_f32",
            0x9 when lo < 8 => $"v_cmpx_{IntCmp[lo]}_i32",
            0x9 when lo == 8 => "v_cmpx_class_f32",
            0xA when lo < 8 => $"v_cmp_{IntCmp[lo]}_i64",
            0xA when lo == 8 => "v_cmp_class_f64",
            0xB when lo < 8 => $"v_cmpx_{IntCmp[lo]}_i64",
            0xC when lo < 8 => $"v_cmp_{IntCmp[lo]}_u32",
            0xD when lo < 8 => $"v_cmpx_{IntCmp[lo]}_u32",
            0xE when lo < 8 => $"v_cmp_{IntCmp[lo]}_u64",
            0xF when lo < 8 => $"v_cmpx_{IntCmp[lo]}_u64",
            0x8 when lo == 15 => "v_cmp_class_f16",
            0x9 when lo == 15 => "v_cmpx_class_f16",
            0x8 => $"v_cmp_{IntCmp[lo - 8]}_i16",
            0x9 => $"v_cmpx_{IntCmp[lo - 8]}_i16",
            0xA => $"v_cmp_{IntCmp[lo - 8]}_u16",
            0xB => $"v_cmpx_{IntCmp[lo - 8]}_u16",
            0xC => $"v_cmp_{FloatCmp[lo - 8]}_f16",
            0xD => $"v_cmpx_{FloatCmp[lo - 8]}_f16",
            0xE => $"v_cmp_{FloatCmp[lo]}_f16",
            0xF => $"v_cmpx_{FloatCmp[lo]}_f16",
            _ => $"v_cmp_op_{op}",
        };
    }

    public static readonly Dictionary<uint, string> Vop3 = Map(
        (0x140, "v_mad_legacy_f32"), (0x141, "v_mad_f32"), (0x342, "v_interp_p1ll_f16"), (0x343, "v_interp_p1lv_f16"),
        (0x351, "v_min3_f16"), (0x352, "v_min3_i16"), (0x353, "v_min3_u16"), (0x354, "v_max3_f16"), (0x355, "v_max3_i16"),
        (0x356, "v_max3_u16"), (0x357, "v_med3_f16"), (0x358, "v_med3_i16"), (0x359, "v_med3_u16"),
        (0x35a, "v_interp_p2_f16"), (0x35f, "v_div_fixup_f16"),(0x142, "v_mad_i32_i24"), (0x143, "v_mad_u32_u24"), (0x144, "v_cubeid_f32"),
        (0x145, "v_cubesc_f32"), (0x146, "v_cubetc_f32"), (0x147, "v_cubema_f32"), (0x148, "v_bfe_u32"), (0x149, "v_bfe_i32"),
        (0x14a, "v_bfi_b32"), (0x14b, "v_fma_f32"), (0x14c, "v_fma_f64"), (0x14d, "v_lerp_u8"), (0x14e, "v_alignbit_b32"),
        (0x14f, "v_alignbyte_b32"), (0x151, "v_min3_f32"), (0x152, "v_min3_i32"), (0x153, "v_min3_u32"),
        (0x154, "v_max3_f32"), (0x155, "v_max3_i32"), (0x156, "v_max3_u32"), (0x157, "v_med3_f32"), (0x158, "v_med3_i32"),
        (0x159, "v_med3_u32"), (0x15a, "v_sad_u8"), (0x15b, "v_sad_hi_u8"), (0x15c, "v_sad_u16"), (0x15d, "v_sad_u32"),
        (0x15e, "v_cvt_pk_u8_f32"), (0x15f, "v_div_fixup_f32"), (0x160, "v_div_fixup_f64"), (0x164, "v_add_f64"),
        (0x165, "v_mul_f64"), (0x166, "v_min_f64"), (0x167, "v_max_f64"), (0x168, "v_ldexp_f64"), (0x169, "v_mul_lo_u32"),
        (0x16a, "v_mul_hi_u32"), (0x16b, "v_mul_hi_i32"), (0x16d, "v_div_scale_f32"), (0x16e, "v_div_scale_f64"),
        (0x16f, "v_div_fmas_f32"), (0x170, "v_div_fmas_f64"), (0x171, "v_msad_u8"), (0x176, "v_mad_u64_u32"),
        (0x177, "v_mad_i64_i32"), (0x2ff, "v_lshlrev_b64"), (0x300, "v_lshrrev_b64"), (0x301, "v_ashrrev_i64"),
        (0x303, "v_add_nc_u16"), (0x304, "v_sub_nc_u16"), (0x305, "v_mul_lo_u16"), (0x307, "v_lshrrev_b16"),
        (0x308, "v_ashrrev_i16"), (0x309, "v_max_u16"), (0x30a, "v_max_i16"), (0x30b, "v_min_u16"), (0x30c, "v_min_i16"),
        (0x30d, "v_add_nc_i16"), (0x30e, "v_sub_nc_i16"), (0x311, "v_pack_b32_f16"), (0x314, "v_lshlrev_b16"),
        (0x340, "v_mad_u16"), (0x344, "v_perm_b32"), (0x34b, "v_fma_f16"), (0x35e, "v_mad_i16"),
        (0x373, "v_mad_u32_u16"), (0x375, "v_mad_i32_i16"), (0x376, "v_sub_nc_i32"), (0x37f, "v_add_nc_i32"),
(0x200, "v_interp_p1_f32"), (0x201, "v_interp_p2_f32"), (0x202, "v_interp_mov_f32"),
        (0x30f, "v_add_co_u32"), (0x310, "v_sub_co_u32"), (0x319, "v_subrev_co_u32"), (0x345, "v_xad_u32"),
        (0x346, "v_lshl_add_u32"), (0x347, "v_add_lshl_u32"), (0x360, "v_readlane_b32"), (0x361, "v_writelane_b32"),
        (0x362, "v_ldexp_f32"), (0x363, "v_bfm_b32"), (0x364, "v_bcnt_u32_b32"), (0x365, "v_mbcnt_lo_u32_b32"),
        (0x366, "v_mbcnt_hi_u32_b32"), (0x368, "v_cvt_pknorm_i16_f32"), (0x369, "v_cvt_pknorm_u16_f32"),
        (0x36a, "v_cvt_pk_u16_u32"), (0x36b, "v_cvt_pk_i16_i32"), (0x36d, "v_add3_u32"), (0x36f, "v_lshl_or_b32"),
        (0x371, "v_and_or_b32"), (0x372, "v_or3_b32"), (0x377, "v_permlane16_b32"), (0x378, "v_permlanex16_b32"));

    public static readonly HashSet<uint> Vop3TwoSource =
        [0x2ff, 0x300, 0x301, 0x303, 0x304, 0x305, 0x307, 0x308, 0x309, 0x30a, 0x30b, 0x30c, 0x30d, 0x30e, 0x311,
         0x314, 0x376, 0x37f, 0x164, 0x165, 0x166, 0x167, 0x168, 0x169, 0x16a, 0x16b, 0x30f, 0x310, 0x319, 0x360, 0x362, 0x363, 0x364,
         0x365, 0x366, 0x368, 0x369, 0x36a, 0x36b, 0x201, 0x202];

    public static readonly Dictionary<uint, string> Vop3P = Seq(0,
        "v_pk_mad_i16", "v_pk_mul_lo_u16", "v_pk_add_i16", "v_pk_sub_i16", "v_pk_lshlrev_b16", "v_pk_lshrrev_b16",
        "v_pk_ashrrev_i16", "v_pk_max_i16", "v_pk_min_i16", "v_pk_mad_u16", "v_pk_add_u16", "v_pk_sub_u16",
        "v_pk_max_u16", "v_pk_min_u16", "v_pk_fma_f16", "v_pk_add_f16", "v_pk_mul_f16", "v_pk_min_f16", "v_pk_max_f16",
        "v_dot2_f32_f16", "v_dot2_i32_i16", "v_dot2_u32_u16", "v_dot4_i32_i8", "v_dot4_u32_u8", "v_dot8_i32_i4",
        "v_dot8_u32_u4", "", "", "", "", "", "", "v_fma_mix_f32", "v_fma_mixlo_f16", "v_fma_mixhi_f16");

    public static readonly Dictionary<uint, string> Ds = Map(
        (0, "ds_add_u32"), (1, "ds_sub_u32"), (2, "ds_rsub_u32"), (3, "ds_inc_u32"), (4, "ds_dec_u32"), (5, "ds_min_i32"),
        (6, "ds_max_i32"), (7, "ds_min_u32"), (8, "ds_max_u32"), (9, "ds_and_b32"), (10, "ds_or_b32"), (11, "ds_xor_b32"),
        (12, "ds_mskor_b32"), (30, "ds_write_b8"), (31, "ds_write_b16"),(13, "ds_write_b32"), (14, "ds_write2_b32"), (15, "ds_write2st64_b32"),
        (16, "ds_cmpst_b32"), (32, "ds_add_rtn_u32"), (45, "ds_wrxchg_rtn_b32"), (54, "ds_read_b32"), (55, "ds_read2_b32"),
        (56, "ds_read2st64_b32"), (57, "ds_read_i8"), (58, "ds_read_u8"), (59, "ds_read_i16"), (60, "ds_read_u16"),
        (53, "ds_swizzle_b32"), (178, "ds_permute_b32"), (179, "ds_bpermute_b32"), (77, "ds_write_b64"),
        (78, "ds_write2_b64"), (118, "ds_read_b64"), (119, "ds_read2_b64"), (222, "ds_write_b96"), (223, "ds_write_b128"),
        (254, "ds_read_b96"), (255, "ds_read_b128"));

    public static readonly Dictionary<uint, string> Flat = Map(
        (8, "load_ubyte"), (9, "load_sbyte"), (10, "load_ushort"), (11, "load_sshort"), (12, "load_dword"),
        (13, "load_dwordx2"), (14, "load_dwordx4"), (15, "load_dwordx3"), (24, "store_byte"), (25, "store_byte_d16_hi"),
        (26, "store_short"), (27, "store_short_d16_hi"), (28, "store_dword"), (29, "store_dwordx2"), (30, "store_dwordx4"),
        (31, "store_dwordx3"), (48, "atomic_swap"), (49, "atomic_cmpswap"), (50, "atomic_add"), (51, "atomic_sub"),
        (53, "atomic_smin"), (54, "atomic_umin"), (55, "atomic_smax"), (56, "atomic_umax"), (57, "atomic_and"),
        (58, "atomic_or"), (59, "atomic_xor"), (60, "atomic_inc"), (61, "atomic_dec"));

    public static readonly Dictionary<uint, string> Mubuf = Map(
        (0, "buffer_load_format_x"), (1, "buffer_load_format_xy"), (2, "buffer_load_format_xyz"),
        (3, "buffer_load_format_xyzw"), (4, "buffer_store_format_x"), (5, "buffer_store_format_xy"),
        (6, "buffer_store_format_xyz"), (7, "buffer_store_format_xyzw"), (8, "buffer_load_ubyte"), (9, "buffer_load_sbyte"),
        (10, "buffer_load_ushort"), (11, "buffer_load_sshort"), (12, "buffer_load_dword"), (13, "buffer_load_dwordx2"),
        (14, "buffer_load_dwordx4"), (15, "buffer_load_dwordx3"), (24, "buffer_store_byte"), (26, "buffer_store_short"),
        (28, "buffer_store_dword"), (29, "buffer_store_dwordx2"), (30, "buffer_store_dwordx4"), (31, "buffer_store_dwordx3"),
        (48, "buffer_atomic_swap"), (49, "buffer_atomic_cmpswap"), (50, "buffer_atomic_add"), (51, "buffer_atomic_sub"),
        (53, "buffer_atomic_smin"), (54, "buffer_atomic_umin"), (55, "buffer_atomic_smax"), (56, "buffer_atomic_umax"),
        (57, "buffer_atomic_and"), (58, "buffer_atomic_or"), (59, "buffer_atomic_xor"), (60, "buffer_atomic_inc"),
        (61, "buffer_atomic_dec"),
        (113, "buffer_gl0_inv"), (114, "buffer_gl1_inv"));

    public static readonly Dictionary<uint, string> Mimg = Map(
        (0, "image_load"), (1, "image_load_mip"), (2, "image_load_pck"), (3, "image_load_pck_sgn"),
        (4, "image_load_mip_pck"), (5, "image_load_mip_pck_sgn"), (8, "image_store"), (9, "image_store_mip"),
        (10, "image_store_pck"), (11, "image_store_mip_pck"), (14, "image_get_resinfo"), (15, "image_atomic_swap"),
        (16, "image_atomic_cmpswap"), (17, "image_atomic_add"), (18, "image_atomic_sub"), (20, "image_atomic_smin"),
        (21, "image_atomic_umin"), (22, "image_atomic_smax"), (23, "image_atomic_umax"), (24, "image_atomic_and"),
        (25, "image_atomic_or"), (26, "image_atomic_xor"), (27, "image_atomic_inc"), (28, "image_atomic_dec"),
        (32, "image_sample"), (33, "image_sample_cl"), (34, "image_sample_d"), (35, "image_sample_d_cl"),
        (36, "image_sample_l"), (37, "image_sample_b"), (38, "image_sample_b_cl"), (39, "image_sample_lz"),
        (40, "image_sample_c"), (41, "image_sample_c_cl"), (42, "image_sample_c_d"), (43, "image_sample_c_d_cl"),
        (44, "image_sample_c_l"), (45, "image_sample_c_b"), (46, "image_sample_c_b_cl"), (47, "image_sample_c_lz"),
        (48, "image_sample_o"), (49, "image_sample_cl_o"), (50, "image_sample_d_o"), (51, "image_sample_d_cl_o"),
        (52, "image_sample_l_o"), (53, "image_sample_b_o"), (54, "image_sample_b_cl_o"), (55, "image_sample_lz_o"),
        (56, "image_sample_c_o"), (57, "image_sample_c_cl_o"), (58, "image_sample_c_d_o"), (59, "image_sample_c_d_cl_o"),
        (60, "image_sample_c_l_o"), (61, "image_sample_c_b_o"), (62, "image_sample_c_b_cl_o"), (63, "image_sample_c_lz_o"),
        (64, "image_gather4"), (65, "image_gather4_cl"), (68, "image_gather4_l"), (69, "image_gather4_b"),
        (70, "image_gather4_b_cl"), (71, "image_gather4_lz"), (72, "image_gather4_c"), (73, "image_gather4_c_cl"),
        (76, "image_gather4_c_l"), (77, "image_gather4_c_b"), (78, "image_gather4_c_b_cl"), (79, "image_gather4_c_lz"),
        (80, "image_gather4_o"), (81, "image_gather4_cl_o"), (84, "image_gather4_l_o"), (85, "image_gather4_b_o"),
        (86, "image_gather4_b_cl_o"), (87, "image_gather4_lz_o"), (88, "image_gather4_c_o"), (89, "image_gather4_c_cl_o"),
        (92, "image_gather4_c_l_o"), (93, "image_gather4_c_b_o"), (94, "image_gather4_c_b_cl_o"), (95, "image_gather4_c_lz_o"),
        (96, "image_get_lod"), (102, "image_bvh_intersect_ray"), (103, "image_bvh64_intersect_ray"));
}
