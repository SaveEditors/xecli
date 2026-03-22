from __future__ import annotations

import html
import re
from pathlib import Path

import markdown


ROOT = Path(__file__).resolve().parents[1]
WIKI_DIR = ROOT / "wiki"
ASSETS_DIR = WIKI_DIR / "assets"
WIKI_HOME_URL = "https://saveeditors.github.io/xecli/wiki/Home.html"

SITE_GROUPS = [
    {
        "id": "getting-started",
        "title": "Getting Started",
        "pages": [
            ("Home", "Home.md"),
            ("Latest Features", "Latest-Features.md"),
            ("Beginner Guide", "Beginner-Guide.md"),
            ("FTP and File Transfer", "FTP-and-File-Transfer.md"),
            ("Commands Reference", "Commands.md"),
            ("CLI Help Output", "CLI-Help.md"),
            ("Hardware and System Controls", "Hardware-and-System.md"),
            ("Remote Spoofing", "Remote-Spoofing.md"),
            ("XNotify", "XNotify.md"),
            ("Homebrew and USB", "Homebrew-and-USB.md"),
            ("Original Xbox Compatibility", "Original-Xbox-Compatibility.md"),
            ("Fatman", "FATX-Manager.md"),
            ("Avatar Item Collection", "Avatar-Item-Collection.md"),
            ("Troubleshooting", "Troubleshooting.md"),
        ],
    },
    {
        "id": "architecture-reference",
        "title": "Technical Reference",
        "pages": [
            ("Advanced Guide", "Advanced-Guide.md"),
            ("Frameworks and Architecture", "Frameworks.md"),
            ("Documentation Standards", "Standards.md"),
            ("Contributing", "Contributing.md"),
        ],
    },
    {
        "id": "data-and-integrations",
        "title": "Data and Integrations",
        "pages": [
            ("Integrations", "Integrations.md"),
            ("Title ID Database", "Title-ID-Database.md"),
            ("FAQ", "FAQ.md"),
        ],
    },
]

PAGE_META = {}
for group in SITE_GROUPS:
    for title, md_name in group["pages"]:
        PAGE_META[md_name] = {
            "title": title,
            "html_name": md_name.replace(".md", ".html"),
            "group_id": group["id"],
            "group_title": group["title"],
        }


def markdown_to_html(text: str) -> str:
    md = markdown.Markdown(
        extensions=[
            "extra",
            "fenced_code",
            "tables",
            "toc",
            "sane_lists",
        ],
        extension_configs={
            "toc": {"permalink": False},
        },
        output_format="html5",
    )
    rendered = md.convert(text)
    rendered = re.sub(r'href="([^"]+)\.md(#.*?)?"', r'href="\1.html\2"', rendered)
    rendered = re.sub(r"```text", "```", rendered)
    return rendered


def build_sidebar(current_md_name: str) -> str:
    blocks = []
    for group in SITE_GROUPS:
        is_open = "true" if any(md_name == current_md_name for _, md_name in group["pages"]) else "false"
        items = []
        for title, md_name in group["pages"]:
            meta = PAGE_META[md_name]
            current_class = " is-current" if md_name == current_md_name else ""
            items.append(
                f'<a class="wiki-nav__link{current_class}" href="{meta["html_name"]}" data-page="{html.escape(meta["html_name"])}">{html.escape(title)}</a>'
            )
        blocks.append(
            f"""
            <details class="wiki-nav__group" data-group="{html.escape(group["id"])}" {"open" if is_open == "true" else ""}>
              <summary>{html.escape(group["title"])}</summary>
              <div class="wiki-nav__items">
                {''.join(items)}
              </div>
            </details>
            """
        )
    return "\n".join(blocks)


def build_page(md_name: str) -> None:
    source_path = WIKI_DIR / md_name
    meta = PAGE_META[md_name]
    markdown_text = source_path.read_text(encoding="utf-8")
    body = markdown_to_html(markdown_text)
    page_title = meta["title"]
    sidebar_html = build_sidebar(md_name)
    repo_link = "https://github.com/SaveEditors/xecli"
    releases_link = "https://github.com/SaveEditors/xecli/releases/latest"
    author_link = "https://www.se7ensins.com/members/pepe-le-pew.527865/"

    output = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{html.escape(page_title)} | XeCLI Wiki</title>
  <meta name="description" content="XeCLI documentation for Xbox 360 RGH/JTAG live console workflows.">
  <link rel="stylesheet" href="assets/wiki-site.css">
</head>
<body data-page="{html.escape(meta["html_name"])}">
  <div class="wiki-shell">
    <aside class="wiki-sidebar">
      <a class="wiki-brand" href="Home.html">
        <img src="assets/xecli-brand.jpg" alt="XeCLI logo">
        <span>XeCLI Wiki</span>
      </a>
      <p class="wiki-sidebar__tagline">Terminal-first Xbox 360 RGH/JTAG toolkit for XBDM, JRPC2, FTP, XEX dumping, memory inspection, and automation.</p>
      <p class="wiki-sidebar__credit">Created by <a href="{author_link}">Pew7s</a></p>
      <label class="wiki-search">
        <span>Filter Pages</span>
        <input id="wiki-filter" type="text" placeholder="Type to filter navigation">
      </label>
      <nav class="wiki-nav" aria-label="Wiki navigation">
        {sidebar_html}
      </nav>
      <div class="wiki-sidebar__meta">
        <a href="https://github.com/SaveEditors/xecli/releases/latest">Latest Release</a>
        <a href="https://github.com/SaveEditors/xecli">Repo</a>
        <a href="{WIKI_HOME_URL}">Wiki Home</a>
      </div>
    </aside>

    <main class="wiki-main">
      <header class="wiki-topbar">
        <div>
          <div class="wiki-breadcrumbs">XeCLI / {html.escape(meta["group_title"])}</div>
          <h1>{html.escape(page_title)}</h1>
        </div>
        <div class="wiki-actions">
          <a href="{repo_link}">Repo</a>
          <a href="{releases_link}">Releases</a>
        </div>
      </header>

      <section class="wiki-content-card">
        <article id="wiki-content" class="markdown-body">
          {body}
        </article>
      </section>
    </main>

    <aside class="wiki-rightbar">
      <section class="wiki-panel">
        <h2>On This Page</h2>
        <nav id="wiki-toc" class="wiki-toc" aria-label="Table of contents"></nav>
      </section>
      <section class="wiki-panel">
        <h2>Documentation Workflow</h2>
        <ul class="wiki-panel__list">
          <li>Start with <a href="Home.html">Home</a> for page routing.</li>
          <li>Use <a href="Commands.html">Commands Reference</a> for task-level examples.</li>
          <li>Use <a href="CLI-Help.html">CLI Help Output</a> for exact built-in help text.</li>
          <li>Use <a href="Standards.html">Documentation Standards</a> before changing wiki structure.</li>
        </ul>
      </section>
      <section class="wiki-panel wiki-support">
        <h2>Support XeCLI</h2>
        <p class="wiki-support__text">Support our work? Buy us a coffee!</p>
        <p class="wiki-support__button">
          <a href="https://ko-fi.com/saveeditors" target="_blank" rel="noopener noreferrer">
            <img src="https://storage.ko-fi.com/cdn/kofi3.png?v=3" alt="Support XeCLI on Ko-fi" width="180">
          </a>
        </p>
      </section>
    </aside>
  </div>

  <script src="assets/wiki-site.js"></script>
</body>
</html>
"""
    (WIKI_DIR / meta["html_name"]).write_text(output, encoding="utf-8", newline="\n")


def build_redirect() -> None:
    redirect_html = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta http-equiv="refresh" content="0; url=Home.html">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>XeCLI Wiki</title>
  <script>window.location.replace("Home.html");</script>
</head>
<body>
  <p>Redirecting to <a href="Home.html">XeCLI Wiki Home</a>...</p>
</body>
</html>
"""
    (WIKI_DIR / "index.html").write_text(redirect_html, encoding="utf-8", newline="\n")


def main() -> None:
    ASSETS_DIR.mkdir(parents=True, exist_ok=True)
    for md_name in PAGE_META:
        build_page(md_name)
    build_redirect()


if __name__ == "__main__":
    main()
