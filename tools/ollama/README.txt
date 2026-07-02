LLM semantic mode uses Ollama (free, local, no API key).

Setup:
1. Install Ollama from https://ollama.com
2. In a terminal: ollama pull llama3.1:8b
3. Ensure Ollama is running (tray icon or: ollama serve)
4. In HtmlToSlidesPro, select "LLM semantic (Ollama — local)"
5. Default endpoint: http://localhost:11434
6. Default model: llama3.1:8b (fits ~8GB VRAM / RAM)

Other models that work: llama3:8b, mistral, phi3

The LLM receives a semantic JSON tree (sections, headings, colors, cards)
and returns a constrained slide-plan JSON rendered as editable PowerPoint shapes.
