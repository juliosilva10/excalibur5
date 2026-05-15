import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { readFileSync, existsSync } from "fs";
import { join } from "path";

const LOG_PATH = join(
  process.env.LOCALAPPDATA || "",
  "Excalibur5",
  "logs",
  "excalibur5.log"
);

function readLogLines() {
  if (!existsSync(LOG_PATH)) return [];
  const content = readFileSync(LOG_PATH, "utf-8");
  return content.split("\n").filter((l) => l.length > 0);
}

const server = new McpServer({
  name: "excalibur5-logs",
  version: "1.0.0",
});

server.tool(
  "read_logs",
  "Read the last N lines from Excalibur5 log file, optionally filtered by keyword",
  { lines: z.number().default(50), filter: z.string().optional() },
  async ({ lines, filter }) => {
    const allLines = readLogLines();
    let result = allLines.slice(-lines);
    if (filter) {
      result = result.filter((l) => l.toLowerCase().includes(filter.toLowerCase()));
    }
    return {
      content: [
        {
          type: "text",
          text: result.length > 0
            ? `[${LOG_PATH}] (${result.length} lines)\n\n${result.join("\n")}`
            : `No log entries found${filter ? ` matching "${filter}"` : ""}.`,
        },
      ],
    };
  }
);

server.tool(
  "read_logs_since",
  "Read log entries since a given timestamp (HH:mm:ss), optionally filtered",
  { since: z.string(), filter: z.string().optional() },
  async ({ since, filter }) => {
    const allLines = readLogLines();
    let result = allLines.filter((l) => l.substring(0, 8) >= since);
    if (filter) {
      result = result.filter((l) => l.toLowerCase().includes(filter.toLowerCase()));
    }
    return {
      content: [
        {
          type: "text",
          text: result.length > 0
            ? `[Since ${since}] (${result.length} lines)\n\n${result.join("\n")}`
            : `No log entries found since ${since}${filter ? ` matching "${filter}"` : ""}.`,
        },
      ],
    };
  }
);

server.tool(
  "search_logs",
  "Search log entries by regex pattern with optional context lines",
  { pattern: z.string(), context: z.number().default(2) },
  async ({ pattern, context }) => {
    const allLines = readLogLines();
    const regex = new RegExp(pattern, "i");
    const matches = [];

    for (let i = 0; i < allLines.length; i++) {
      if (regex.test(allLines[i])) {
        const start = Math.max(0, i - context);
        const end = Math.min(allLines.length - 1, i + context);
        const block = allLines.slice(start, end + 1).map((l, idx) => {
          const lineNum = start + idx;
          return lineNum === i ? `>>> ${l}` : `    ${l}`;
        });
        matches.push(block.join("\n"));
      }
    }

    return {
      content: [
        {
          type: "text",
          text: matches.length > 0
            ? `[${matches.length} matches for /${pattern}/i]\n\n${matches.join("\n---\n")}`
            : `No matches found for /${pattern}/i.`,
        },
      ],
    };
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
