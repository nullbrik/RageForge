# Legal notice

## Project scope

RageForge is an independent, open-source research and visualization toolkit for RAGE engine file formats, including RPF9 and TTPF. It is not affiliated with, endorsed by, or sponsored by Rockstar Games or Take-Two Interactive. Game content and trademarks remain subject to their respective owners' rights.

The current release is intended for local inspection of lawfully accessible files. It contains no asset export or raw extraction features, does not distribute game files or encryption keys, and does not modify original archives. It identifies non-encrypted resources using file carving and standard decompression, without decrypting or reconstructing the encrypted RPF9 table of contents (TOC). Displayed names are obtained from resource metadata or generated independently.

Read-only refers to the original input files, not to an absence of all writes: RageForge writes a local index cache, settings, a crash log and, only when explicitly enabled, a benchmark log, as listed in the README. These files hold metadata, not game resources. Resources are decoded in memory for display and are not written to disk. At startup the application checks GitHub for a newer release; it sends no game or user data and does not download or install updates itself.

## French and European legal framework

The following references describe relevant rights and limitations. They are not a determination that any specific operation performed with RageForge is lawful.

### Software study and interoperability

- [Directive 2009/24/EC, Articles 5(3), 6 and 8(2)](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32009L0024): a person entitled to use a copy of a program may observe, study or test its functioning during operations they are entitled to perform. Decompilation is permitted under specific conditions when indispensable to interoperability of an independently created program. Contractual provisions contrary to Article 6 or the exceptions in Article 5(2) and (3) are void.
- [French Intellectual Property Code, Article L122-6-1](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000028345224): implements rights and exceptions concerning software use, backup, study and interoperability, subject to their statutory conditions. Interoperability information must not be used or disclosed beyond the purposes permitted by the provision.
- [CJEU, SAS Institute v World Programming, C-406/10, 2 May 2012](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:62010CJ0406): program functionality, programming languages and data file formats are not, as such, forms of expression protected by software copyright. This does not exclude other copyright protection for original works or authorize copying protected game assets.

### Technical protection measures and game content

- [Directive 2001/29/EC, Article 6](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32001L0029): provides protection against circumvention of effective technological measures and certain related devices, products and services.
- [French Intellectual Property Code, Article L331-5](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000044259273): defines effective technical measures for protected works other than software. A protocol, format or encryption method is not by itself a technical measure within the meaning of the article.
- [Article L335-3-1](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000006279236): provides sanctions for specified acts against effective technical measures and for knowingly supplying certain means designed or adapted for such acts. Research-related exceptions are limited to the conditions in the statute.
- [CJEU, Nintendo v PC Box, C-355/12, 23 January 2014](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:62012CJ0355): recognizes the complex nature of video games and requires consideration of proportionality and legitimate uses when assessing technological measures.

The absence of decryption does not, by itself, settle whether an alternative method of accessing resources constitutes circumvention. The role and effectiveness of the measure, the operations performed and the origin of the data are relevant. Software-related exceptions do not automatically extend to graphical, audio or audiovisual game content.

## Responsible use and contributions

The project does not support leaked or unlawfully obtained game files, piracy, unauthorized redistribution, obtaining encryption keys, porting game content into other games, or multiplayer integrations. Users must assess their lawful access to input files and applicable rights, including any mandatory statutory exceptions and enforceable contractual terms. Purchasing a game does not automatically authorize every method of obtaining or using its resources.

See [Rockstar's mod guidelines](https://www.rockstargames.com/community-resources/mod-guidelines). RageForge does not claim approval or coverage under those guidelines. Contribution rules are described in [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence and warranty

RageForge is licensed under LGPL-3.0-only; see [LICENSE](LICENSE) and [COPYING](COPYING). Third-party components retain their respective licences; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The software is provided without warranty as set out in its licence, subject to applicable law. The software licence does not grant rights over third-party game content.

This notice is informational and does not restrict rights granted by the software licence or mandatory law. It is not legal advice or a guarantee of compliance.

## Contact

Rights holders may contact the maintainer through GitHub to raise a specific concern. Please do not attach proprietary game files or encryption keys. For vulnerabilities in RageForge itself, see [SECURITY.md](SECURITY.md).
