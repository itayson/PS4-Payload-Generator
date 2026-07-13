package com.akirabox.android;

final class PayloadCatalog {
    static final String GOLDHEN_SHOP_URL = "https://ko-fi.com/sistro/shop";

    static final GoldHenEntry[] GOLDHEN = {
            new GoldHenEntry("GoldHEN v2.4b18.10", "Mais recente no catálogo oficial", "https://ko-fi.com/s/d64a916507"),
            new GoldHenEntry("GoldHEN v2.4b18.9", "Inclui suporte ao firmware 13.00", "https://ko-fi.com/s/407bb9c94a"),
            new GoldHenEntry("GoldHEN v2.4b18.8", "Game Patch Menu e melhorias no PayLoader", "https://ko-fi.com/s/969702e773"),
            new GoldHenEntry("GoldHEN v2.4b18.7", "Versão oficial do Ko-fi", "https://ko-fi.com/s/5d29f9e29c"),
            new GoldHenEntry("GoldHEN v2.4b18.6", "Versão oficial do Ko-fi", "https://ko-fi.com/s/a89f831c81"),
            new GoldHenEntry("GoldHEN v2.4b18.5", "Versão oficial do Ko-fi", "https://ko-fi.com/s/bd655acbdb"),
            new GoldHenEntry("GoldHEN v2.4b18.4", "Adiciona suporte a 12.00 e 12.02", "https://ko-fi.com/s/4f03d81833"),
            new GoldHenEntry("GoldHEN v2.4b18.3", "Versão oficial do Ko-fi", "https://ko-fi.com/s/39f14f8d18"),
            new GoldHenEntry("GoldHEN v2.4b18.2", "Versão oficial do Ko-fi", "https://ko-fi.com/s/ef128b9879"),
            new GoldHenEntry("GoldHEN v2.4b18", "Base pública da série b18", "https://ko-fi.com/s/45be0776ec"),
            new GoldHenEntry("GoldHEN v2.4b17.3", "Versão oficial do Ko-fi", "https://ko-fi.com/s/3aa1f4d1f0"),
            new GoldHenEntry("GoldHEN v2.4b17.2", "Versão oficial do Ko-fi", "https://ko-fi.com/s/f53a28e7d2"),
            new GoldHenEntry("GoldHEN v2.4b17", "Primeira série com loaders de kernel", "https://ko-fi.com/s/0fa43df49e"),
            new GoldHenEntry("GoldHEN v2.4b16.2", "Versão oficial do Ko-fi", "https://ko-fi.com/s/10736395e4"),
            new GoldHenEntry("GoldHEN v2.4b16", "Versão oficial do Ko-fi", "https://ko-fi.com/s/c115adcfb6"),
            new GoldHenEntry("GoldHEN v2.4b14", "Versão oficial do Ko-fi", "https://ko-fi.com/s/92300b8d04"),
            new GoldHenEntry("GoldHEN v2.4b13", "Versão oficial do Ko-fi", "https://ko-fi.com/s/82e3cab517"),
            new GoldHenEntry("GoldHEN v2.4b5", "Versão oficial do Ko-fi", "https://ko-fi.com/s/8089b4c5b2"),
            new GoldHenEntry("GoldHEN v2.3 para 7.02 / 7.55", "Edição oficial para firmwares antigos", "https://ko-fi.com/s/922df3f5d8"),
            new GoldHenEntry("GoldHEN v2.2.5b7", "Versão oficial do Ko-fi", "https://ko-fi.com/s/02a27d9788"),
            new GoldHenEntry("GoldHEN v2.2.5b2", "Versão oficial do Ko-fi", "https://ko-fi.com/s/bd8565847d")
    };

    static final ReadyPayload[] READY = {
            new ReadyPayload(
                    "NP Fake Signin 1.1 • PS4",
                    false,
                    "ELF oficial pré-configurado",
                    "https://raw.githubusercontent.com/earthonion/np-fake-signin/1.1/bin/np-fake-signin-ps4.elf",
                    true,
                    "Requer uma conta já ativada offline com Account ID diferente de zero. Reinicie o console depois da execução."
            ),
            new ReadyPayload(
                    "NP Fake Signin 1.1 • PS5",
                    true,
                    "ELF oficial pré-configurado",
                    "https://raw.githubusercontent.com/earthonion/np-fake-signin/1.1/bin/np-fake-signin-ps5.elf",
                    true,
                    "Requer uma conta já ativada offline com Account ID diferente de zero. Reinicie o console depois da execução."
            ),
            new ReadyPayload(
                    "FTP Server 0.20 • PS4",
                    false,
                    "Servidor FTP oficial pré-configurado",
                    "https://github.com/ps5-payload-dev/ftpsrv/releases/download/v0.20/ftpsrv-ps4.elf",
                    true,
                    "Use somente no seu próprio console e não exponha a porta FTP à internet."
            ),
            new ReadyPayload(
                    "FTP Server 0.20 • PS5",
                    true,
                    "Servidor FTP oficial pré-configurado",
                    "https://github.com/ps5-payload-dev/ftpsrv/releases/download/v0.20/ftpsrv-ps5.elf",
                    true,
                    "Use somente no seu próprio console e não exponha a porta FTP à internet."
            ),
            new ReadyPayload(
                    "PS4Debug 1.1.19 • PS4",
                    false,
                    "BIN oficial pré-configurado",
                    "https://raw.githubusercontent.com/GoldHEN/ps4debug/v1.1.19/ps4debug.bin",
                    true,
                    "Compatível com os firmwares indicados na release oficial, de 5.05 a 12.02."
            )
    };

    private PayloadCatalog() {}

    static final class GoldHenEntry {
        final String name;
        final String detail;
        final String url;

        GoldHenEntry(String name, String detail, String url) {
            this.name = name;
            this.detail = detail;
            this.url = url;
        }
    }

    static final class ReadyPayload {
        final String name;
        final boolean ps5;
        final String detail;
        final String url;
        final boolean direct;
        final String requirements;

        ReadyPayload(
                String name,
                boolean ps5,
                String detail,
                String url,
                boolean direct,
                String requirements
        ) {
            this.name = name;
            this.ps5 = ps5;
            this.detail = detail;
            this.url = url;
            this.direct = direct;
            this.requirements = requirements;
        }
    }
}
